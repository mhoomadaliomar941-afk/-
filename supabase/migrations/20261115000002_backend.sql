-- Backend additions on top of the tested core schema (20261115000001_init.sql):
-- what the app reads, sign-up, the fairness rule tied to the public beacon,
-- admin opening of products, and account deletion.

-- ---------------------------------------------------------------- columns the app shows

alter table products add column category text not null default 'phone'
  check (category in ('phone','console','watch','audio','laptop','tablet'));
alter table products add column filled_at timestamptz;
alter table partner_stores add column category text not null default '';

-- the moment the last spot is taken fixes which beacon round picks the owner
create or replace function set_filled_at() returns trigger
language plpgsql as $$
begin
  if new.status = 'full' and old.status is distinct from 'full' then
    new.filled_at := now();
  end if;
  return new;
end $$;
create trigger t_products_filled before update on products for each row execute function set_filled_at();

-- ---------------------------------------------------------------- sign-up

-- Every new auth user (phone OTP) gets a profile and an empty wallet.
create or replace function handle_new_user() returns trigger
language plpgsql security definer set search_path = public as $$
begin
  insert into profiles (id, phone)
  values (new.id, case when new.phone like '+%' then new.phone else '+' || coalesce(new.phone, new.id::text) end)
  on conflict (id) do nothing;
  insert into wallets (user_id) values (new.id) on conflict do nothing;
  return new;
end $$;
create trigger on_auth_user_created after insert on auth.users for each row execute function handle_new_user();

-- ---------------------------------------------------------------- fairness: the round is fixed, not chosen

-- drand default ("mainnet") chain: genesis 1595431050, a round every 30 s.
-- Verify both numbers at https://api.drand.sh/info before launch.
create or replace function drand_round_after(p_at timestamptz) returns bigint
language sql immutable as $$
  select floor((extract(epoch from p_at) - 1595431050) / 30)::bigint + 2
$$;
-- (+1 makes it the round current at p_at; +2 the first round produced after it.)

-- Same as before, plus: the beacon round must be exactly the first one after the
-- product filled, so nobody (including the operator) can shop for a round.
create or replace function assign_owner(p_product uuid, p_round bigint, p_beacon text)
returns assignments
language plpgsql security definer set search_path = public as $$
declare
  prod products;
  seed text;
  win int;
  owner uuid;
  a assignments;
  r record;
begin
  select * into prod from products where id = p_product for update;
  if not found then raise exception 'product not found'; end if;
  if prod.status = 'assigned' then
    select * into a from assignments where product_id = p_product;
    return a;
  end if;
  if prod.status <> 'full' then raise exception 'product is %, not full', prod.status; end if;
  if prod.filled_at is null then raise exception 'product has no filled_at'; end if;
  if p_round <> drand_round_after(prod.filled_at) then
    raise exception 'beacon round must be %, got %', drand_round_after(prod.filled_at), p_round;
  end if;
  if p_beacon !~ '^[0-9a-f]{64}$' then raise exception 'beacon value must be 64 hex chars'; end if;

  select server_seed into seed from product_seeds where product_id = p_product;
  if seed is null then raise exception 'no seed for product'; end if;
  if encode(digest(seed, 'sha256'), 'hex') <> prod.seed_hash then raise exception 'seed does not match published hash'; end if;

  win := ((('x' || substr(encode(digest(seed || p_beacon, 'sha256'), 'hex'), 1, 15))::bit(60)::bigint
          % prod.total_spots) + 1)::int;
  select user_id into owner from spots where product_id = p_product and number = win;

  insert into assignments (product_id, winning_spot, owner_id, server_seed, beacon_round, beacon_value)
  values (p_product, win, owner, seed, p_round, p_beacon) returning * into a;

  update products set status = 'assigned' where id = p_product;
  insert into orders (assignment_id, user_id) values (a.id, owner);

  for r in select * from participations where product_id = p_product and user_id <> owner loop
    perform _ledger(r.user_id, 'shopping', 'credit', r.amount_fils, 'participation', r.id, 'credit:' || r.id);
    update participations set status = 'credited' where id = r.id;
  end loop;
  update participations set status = 'owner' where product_id = p_product and user_id = owner;

  -- one notification per member, not per participation
  insert into notifications (user_id, type, payload)
  select distinct user_id, 'credited', jsonb_build_object('product_id', p_product)
  from participations where product_id = p_product and user_id <> owner;
  insert into notifications (user_id, type, payload)
  values (owner, 'owner', jsonb_build_object('product_id', p_product, 'assignment_id', a.id));

  return a;
end $$;
revoke all on function assign_owner from public, anon, authenticated;

-- ---------------------------------------------------------------- admin: open a product

-- Generates the secret seed and publishes its hash before anyone can buy.
create or replace function open_product(p_product uuid) returns products
language plpgsql security definer set search_path = public as $$
declare prod products; seed text;
begin
  select * into prod from products where id = p_product for update;
  if not found then raise exception 'product not found'; end if;
  if prod.status <> 'draft' then raise exception 'product is %, not draft', prod.status; end if;
  seed := encode(gen_random_bytes(32), 'hex');
  insert into product_seeds (product_id, server_seed) values (p_product, seed)
  on conflict (product_id) do update set server_seed = excluded.server_seed;
  update products set seed_hash = encode(digest(seed, 'sha256'), 'hex'), status = 'open', opens_at = now()
   where id = p_product returning * into prod;
  insert into notifications (user_id, type, payload)
  select id, 'product_open', jsonb_build_object('product_id', p_product) from profiles where phone not like 'deleted:%';
  return prod;
end $$;
revoke all on function open_product from public, anon, authenticated;

-- ---------------------------------------------------------------- reads for the app

-- security_invoker: the caller's RLS applies, so members see only their own rows.
create view my_participations with (security_invoker = true) as
select pa.*, coalesce(array_agg(s.number order by s.number) filter (where s.number is not null), '{}') as spot_numbers
from participations pa
left join spots s on s.participation_id = pa.id
group by pa.id;

create view public_showcase with (security_invoker = true) as
select sc.id, sc.display_name, sc.photo_path, sc.quote, sc.published_at,
       p.title_ar as product_title, p.category
from owner_showcase sc
join assignments a on a.id = sc.assignment_id
join products p on p.id = a.product_id
where sc.published_at is not null;

-- An assignment says who won. This view runs with its owner's rights (no
-- security_invoker) so it can compare owner_id to the caller without members
-- ever being able to read owner_id itself: they get the proof and "is it me".
create view public_assignments as
select id, product_id, winning_spot, server_seed, beacon_round, beacon_value, assigned_at,
       (owner_id = auth.uid()) as is_mine
from assignments;

revoke all on my_participations, public_assignments from anon;
grant select on my_participations, public_assignments to authenticated;
grant select on public_showcase to anon, authenticated;

-- owner ids are not public: replace the open policy with own-row access, the view covers the rest
drop policy assignments_public on assignments;
create policy assignments_read on assignments for select using (true);
revoke select on assignments from anon, authenticated;
grant select (id, product_id, winning_spot, server_seed, beacon_round, beacon_value, assigned_at) on assignments to anon, authenticated;

-- ---------------------------------------------------------------- account deletion

-- Apple requires in-app deletion. Money records stay (legal retention) but are
-- detached from the person: phone and name removed, addresses scrubbed,
-- tokens and notifications deleted. A balance must be zero first, or support
-- pays it out. The Edge Function then soft-deletes the auth user.
create or replace function request_account_deletion() returns void
language plpgsql security definer set search_path = public as $$
declare uid uuid := auth.uid(); w wallets;
begin
  if uid is null then raise exception 'not signed in'; end if;
  select * into w from wallets where user_id = uid for update;
  if coalesce(w.cash_fils, 0) > 0 or coalesce(w.shopping_fils, 0) > 0 then
    raise exception 'balance_not_zero';
  end if;
  if exists (select 1 from participations where user_id = uid and status = 'active') then
    raise exception 'active_participation';
  end if;
  if exists (select 1 from orders where user_id = uid and status in ('awaiting_address','pending','shipped')) then
    raise exception 'open_order';
  end if;
  update profiles set phone = 'deleted:' || uid, full_name = null where id = uid;
  delete from push_tokens where user_id = uid;
  delete from notifications where user_id = uid;
  update addresses set governorate = '-', city = '-', area = null, street = null, building = null, notes = null, phone = '-'
   where user_id = uid;
  delete from owner_showcase where assignment_id in (select id from assignments where owner_id = uid);
end $$;
revoke all on function request_account_deletion from public, anon;
grant execute on function request_account_deletion to authenticated;

-- ---------------------------------------------------------------- defence in depth: client write grants
-- RLS already blocks these (no write policies), but Supabase grants table-level
-- write to anon/authenticated by default. Take it away so a forgotten policy can
-- never open a money table, and so attempts fail loudly instead of updating 0 rows.
revoke insert, update, delete on products, product_seeds, wallets, wallet_txns, payments, participations, spots,
  assignments, orders, owner_showcase, partner_stores, partner_staff, redemptions from anon, authenticated;
revoke all on product_seeds from anon, authenticated;
revoke insert, update, delete on notifications from anon, authenticated;
grant update (read_at) on notifications to authenticated;
revoke all on profiles, addresses, push_tokens, notifications, wallets, wallet_txns, payments, participations, spots,
  orders, redemptions, partner_staff from anon;

-- ---------------------------------------------------------------- server-only functions
-- Revoking from PUBLIC above also removed service_role; the Edge Functions run as service_role.
grant execute on function confirm_topup(uuid), assign_owner(uuid, bigint, text), cancel_product(uuid),
  expire_redemptions(), open_product(uuid), drand_round_after(timestamptz) to service_role;

-- push delivery bookkeeping (the jobs function sends unsent notifications)
alter table notifications add column pushed_at timestamptz;
create index on notifications (created_at) where pushed_at is null;
