-- Schema for the FORTUNEiT-style clone ("community ownership": buy spots,
-- one owner is picked when the product fills, everyone else is credited
-- shopping balance).
--
-- Target: Postgres 15+ on Supabase. Uses auth.users and auth.uid().
-- Money: JOD has 3 decimals, so every amount is an integer in fils
-- (1 JD = 1000 fils) plus a currency column where it can vary.
-- Times: timestamptz, stored in UTC; shown in Asia/Amman.
-- Access: row level security on every table. Clients only READ through RLS;
-- every write that moves money goes through a security-definer function
-- below, so the balance rules live in one place.

create extension if not exists pgcrypto;

-- ---------------------------------------------------------------- helpers

create or replace function set_updated_at() returns trigger
language plpgsql as $$
begin
  new.updated_at := now();
  return new;
end $$;

-- ---------------------------------------------------------------- users

create table profiles (
  id uuid primary key references auth.users(id) on delete cascade,
  phone text not null unique,                       -- E.164, verified by OTP
  full_name text,
  language text not null default 'ar' check (language in ('ar','en')),
  is_admin boolean not null default false,
  created_at timestamptz not null default now(),
  updated_at timestamptz not null default now()
);

create table push_tokens (
  token text primary key,
  user_id uuid not null references profiles(id) on delete cascade,
  platform text not null check (platform in ('android','ios','web')),
  created_at timestamptz not null default now()
);
create index on push_tokens (user_id);

-- ---------------------------------------------------------------- catalogue

create table products (
  id uuid primary key default gen_random_uuid(),
  title_ar text not null,
  title_en text not null,
  brand text,
  description_ar text not null default '',
  description_en text not null default '',
  specs jsonb not null default '{}',
  images text[] not null default '{}',              -- storage paths
  currency char(3) not null default 'JOD',
  retail_price_fils bigint not null check (retail_price_fils > 0),
  spot_price_fils bigint not null check (spot_price_fils >= 1000),
  total_spots int not null check (total_spots > 1),
  filled_spots int not null default 0,
  max_spots_per_user int check (max_spots_per_user is null or max_spots_per_user > 0),
  status text not null default 'draft'
    check (status in ('draft','open','full','assigned','delivered','cancelled')),
  opens_at timestamptz,
  closes_at timestamptz,                            -- optional deadline; unfilled -> cancelled + refund
  -- fairness: sha256 of a secret seed is published when the product opens;
  -- the seed itself is stored in product_seeds, readable only by the server.
  seed_hash text,
  created_at timestamptz not null default now(),
  updated_at timestamptz not null default now(),
  constraint filled_in_range check (filled_spots between 0 and total_spots),
  constraint open_needs_hash check (status = 'draft' or seed_hash is not null)
);
create index on products (status, opens_at desc);

create table product_seeds (                        -- no RLS policy: server only
  product_id uuid primary key references products(id) on delete cascade,
  server_seed text not null,
  created_at timestamptz not null default now()
);

-- ---------------------------------------------------------------- wallet

-- Two buckets: 'cash' (topped up, spends on spots) and 'shopping'
-- (credited when you are not picked, spends at partner stores).
-- Assumption from the recon: the original keeps these separate.
create table wallets (
  user_id uuid primary key references profiles(id) on delete cascade,
  cash_fils bigint not null default 0 check (cash_fils >= 0),
  shopping_fils bigint not null default 0 check (shopping_fils >= 0),
  updated_at timestamptz not null default now()
);

create table wallet_txns (                          -- append-only ledger
  id uuid primary key default gen_random_uuid(),
  user_id uuid not null references profiles(id) on delete restrict,
  bucket text not null check (bucket in ('cash','shopping')),
  type text not null check (type in ('topup','spend','credit','redeem','refund','adjust')),
  amount_fils bigint not null check (amount_fils <> 0),   -- signed
  ref_type text,
  ref_id uuid,
  idempotency_key text unique,
  created_at timestamptz not null default now()
);
create index on wallet_txns (user_id, created_at desc);
create index on wallet_txns (ref_type, ref_id);

create table payments (                             -- wallet top-ups via the gateway
  id uuid primary key default gen_random_uuid(),
  user_id uuid not null references profiles(id) on delete restrict,
  gateway text not null,                            -- 'hyperpay', 'cliq', ...
  amount_fils bigint not null check (amount_fils > 0),
  currency char(3) not null default 'JOD',
  status text not null default 'pending'
    check (status in ('pending','paid','failed','refunded')),
  external_ref text unique,                         -- gateway checkout / transaction id
  raw jsonb not null default '{}',
  created_at timestamptz not null default now(),
  updated_at timestamptz not null default now()
);
create index on payments (user_id, created_at desc);

-- ---------------------------------------------------------------- participation

create table participations (
  id uuid primary key default gen_random_uuid(),
  user_id uuid not null references profiles(id) on delete restrict,
  product_id uuid not null references products(id) on delete restrict,
  spots int not null check (spots > 0),
  amount_fils bigint not null check (amount_fils > 0),
  status text not null default 'active'
    check (status in ('active','owner','credited','refunded')),
  idempotency_key text not null unique,
  created_at timestamptz not null default now()
);
create index on participations (user_id, created_at desc);
create index on participations (product_id);

-- one row per numbered spot; the winning number picks the owner
create table spots (
  product_id uuid not null references products(id) on delete restrict,
  number int not null check (number > 0),
  participation_id uuid not null references participations(id) on delete restrict,
  user_id uuid not null references profiles(id) on delete restrict,
  primary key (product_id, number)
);
create index on spots (participation_id);
create index on spots (user_id);

create table assignments (
  id uuid primary key default gen_random_uuid(),
  product_id uuid not null unique references products(id) on delete restrict,
  winning_spot int not null,
  owner_id uuid not null references profiles(id) on delete restrict,
  server_seed text not null,                        -- revealed: anyone can check sha256 = products.seed_hash
  beacon_round bigint not null,                     -- public randomness (e.g. drand) fetched after the product filled
  beacon_value text not null,
  assigned_at timestamptz not null default now(),
  foreign key (product_id, winning_spot) references spots(product_id, number)
);
create index on assignments (owner_id);

-- ---------------------------------------------------------------- delivery

create table addresses (
  id uuid primary key default gen_random_uuid(),
  user_id uuid not null references profiles(id) on delete cascade,
  governorate text not null,
  city text not null,
  area text,
  street text,
  building text,
  notes text,
  phone text not null,
  is_default boolean not null default false,
  created_at timestamptz not null default now(),
  updated_at timestamptz not null default now()
);
create index on addresses (user_id);
create unique index one_default_address on addresses (user_id) where is_default;

create table orders (
  id uuid primary key default gen_random_uuid(),
  assignment_id uuid not null unique references assignments(id) on delete restrict,
  user_id uuid not null references profiles(id) on delete restrict,
  address_id uuid references addresses(id) on delete restrict,
  status text not null default 'awaiting_address'
    check (status in ('awaiting_address','pending','shipped','delivered','failed')),
  tracking text,
  shipped_at timestamptz,
  delivered_at timestamptz,
  created_at timestamptz not null default now(),
  updated_at timestamptz not null default now(),
  constraint address_after_waiting check (status = 'awaiting_address' or address_id is not null)
);
create index on orders (user_id);
create index on orders (status);

create table owner_showcase (
  id uuid primary key default gen_random_uuid(),
  assignment_id uuid not null unique references assignments(id) on delete cascade,
  display_name text not null,
  photo_path text,
  quote text,
  consent_at timestamptz not null,                  -- no row without the owner's consent
  published_at timestamptz,
  created_at timestamptz not null default now()
);
create index on owner_showcase (published_at desc);

-- ---------------------------------------------------------------- partners

create table partner_stores (
  id uuid primary key default gen_random_uuid(),
  name_ar text not null,
  name_en text not null,
  governorate text not null,
  city text not null,
  logo_path text,
  active boolean not null default true,
  created_at timestamptz not null default now(),
  updated_at timestamptz not null default now()
);
create index on partner_stores (active, governorate);

create table partner_staff (
  store_id uuid not null references partner_stores(id) on delete cascade,
  user_id uuid not null references profiles(id) on delete cascade,
  primary key (store_id, user_id)
);
create index on partner_staff (user_id);

create table redemptions (
  id uuid primary key default gen_random_uuid(),
  user_id uuid not null references profiles(id) on delete restrict,
  store_id uuid not null references partner_stores(id) on delete restrict,
  amount_fils bigint not null check (amount_fils > 0),
  code text not null unique,                        -- shown as QR, scanned by store staff
  status text not null default 'pending'
    check (status in ('pending','confirmed','expired','cancelled')),
  expires_at timestamptz not null,
  confirmed_by uuid references profiles(id) on delete set null,
  confirmed_at timestamptz,
  created_at timestamptz not null default now()
);
create index on redemptions (user_id, created_at desc);
create index on redemptions (store_id, created_at desc);
create index on redemptions (status, expires_at);

-- ---------------------------------------------------------------- notifications

create table notifications (
  id uuid primary key default gen_random_uuid(),
  user_id uuid not null references profiles(id) on delete cascade,
  type text not null,                               -- 'owner', 'credited', 'product_open', 'order_shipped', ...
  payload jsonb not null default '{}',
  read_at timestamptz,
  created_at timestamptz not null default now()
);
create index on notifications (user_id, created_at desc);

-- ---------------------------------------------------------------- triggers

create trigger t_profiles_upd before update on profiles for each row execute function set_updated_at();
create trigger t_products_upd before update on products for each row execute function set_updated_at();
create trigger t_payments_upd before update on payments for each row execute function set_updated_at();
create trigger t_addresses_upd before update on addresses for each row execute function set_updated_at();
create trigger t_orders_upd before update on orders for each row execute function set_updated_at();
create trigger t_stores_upd before update on partner_stores for each row execute function set_updated_at();

-- ---------------------------------------------------------------- money functions

-- Ledger write + balance change in one place. Idempotent on key.
create or replace function _ledger(
  p_user uuid, p_bucket text, p_type text, p_amount bigint,
  p_ref_type text, p_ref_id uuid, p_key text
) returns boolean
language plpgsql security definer set search_path = public as $$
begin
  insert into wallet_txns (user_id, bucket, type, amount_fils, ref_type, ref_id, idempotency_key)
  values (p_user, p_bucket, p_type, p_amount, p_ref_type, p_ref_id, p_key)
  on conflict (idempotency_key) do nothing;
  if not found then
    return false;                                   -- already applied
  end if;
  insert into wallets (user_id) values (p_user) on conflict do nothing;
  if p_bucket = 'cash' then
    update wallets set cash_fils = cash_fils + p_amount, updated_at = now() where user_id = p_user;
  else
    update wallets set shopping_fils = shopping_fils + p_amount, updated_at = now() where user_id = p_user;
  end if;                                           -- check constraint rejects a negative balance
  return true;
end $$;
revoke all on function _ledger from public, anon, authenticated;

-- Called by the payment webhook (service role) once the gateway confirms.
create or replace function confirm_topup(p_payment uuid) returns void
language plpgsql security definer set search_path = public as $$
declare pay payments;
begin
  select * into pay from payments where id = p_payment for update;
  if not found then raise exception 'payment not found'; end if;
  if pay.status = 'paid' then return; end if;      -- webhook arrived twice
  if pay.status <> 'pending' then raise exception 'payment % is %', p_payment, pay.status; end if;
  update payments set status = 'paid' where id = p_payment;
  perform _ledger(pay.user_id, 'cash', 'topup', pay.amount_fils, 'payment', pay.id, 'topup:' || pay.id);
end $$;
revoke all on function confirm_topup from public, anon, authenticated;

-- The core action. Called by the app as the signed-in user.
-- Locks the product row, so two people can never take the same last spot.
create or replace function buy_spots(p_product uuid, p_spots int, p_key text)
returns participations
language plpgsql security definer set search_path = public as $$
declare
  uid uuid := auth.uid();
  prod products;
  part participations;
  mine int;
  cost bigint;
begin
  if uid is null then raise exception 'not signed in'; end if;
  if p_spots is null or p_spots < 1 then raise exception 'spots must be at least 1'; end if;

  select * into part from participations where idempotency_key = p_key;
  if found then
    if part.user_id <> uid then raise exception 'idempotency key in use'; end if;
    return part;                                    -- retry of a request that already succeeded
  end if;

  select * into prod from products where id = p_product for update;
  if not found then raise exception 'product not found'; end if;
  if prod.status <> 'open' then raise exception 'product is not open'; end if;
  if prod.closes_at is not null and prod.closes_at <= now() then raise exception 'product closed'; end if;
  if prod.filled_spots + p_spots > prod.total_spots then
    raise exception 'only % spots left', prod.total_spots - prod.filled_spots;
  end if;
  if prod.max_spots_per_user is not null then
    select coalesce(sum(spots), 0) into mine from participations where user_id = uid and product_id = p_product;
    if mine + p_spots > prod.max_spots_per_user then raise exception 'limit is % spots per member', prod.max_spots_per_user; end if;
  end if;

  cost := prod.spot_price_fils * p_spots;
  insert into participations (user_id, product_id, spots, amount_fils, idempotency_key)
  values (uid, p_product, p_spots, cost, p_key) returning * into part;

  begin
    perform _ledger(uid, 'cash', 'spend', -cost, 'participation', part.id, 'spend:' || part.id);
  exception when check_violation then
    raise exception 'insufficient balance';
  end;

  insert into spots (product_id, number, participation_id, user_id)
  select p_product, n, part.id, uid
  from generate_series(prod.filled_spots + 1, prod.filled_spots + p_spots) n;

  update products
     set filled_spots = filled_spots + p_spots,
         status = case when filled_spots + p_spots = total_spots then 'full' else status end
   where id = p_product;

  return part;
end $$;
revoke all on function buy_spots from public, anon;
grant execute on function buy_spots to authenticated;

-- Picks the owner of a full product. Called by a job (service role) with a
-- public randomness beacon value fetched AFTER the product filled.
-- winning spot = (first 8 bytes of sha256(server_seed || beacon_value)) mod total + 1
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

  -- everyone else gets their spend back as shopping balance
  for r in select * from participations where product_id = p_product and user_id <> owner loop
    perform _ledger(r.user_id, 'shopping', 'credit', r.amount_fils, 'participation', r.id, 'credit:' || r.id);
    update participations set status = 'credited' where id = r.id;
    insert into notifications (user_id, type, payload)
    values (r.user_id, 'credited', jsonb_build_object('product_id', p_product, 'amount_fils', r.amount_fils));
  end loop;
  update participations set status = 'owner' where product_id = p_product and user_id = owner;
  insert into notifications (user_id, type, payload)
  values (owner, 'owner', jsonb_build_object('product_id', p_product, 'assignment_id', a.id));

  return a;
end $$;
revoke all on function assign_owner from public, anon, authenticated;

-- Product passed its deadline without filling: refund everyone in cash.
create or replace function cancel_product(p_product uuid) returns void
language plpgsql security definer set search_path = public as $$
declare prod products; r record;
begin
  select * into prod from products where id = p_product for update;
  if not found then raise exception 'product not found'; end if;
  if prod.status = 'cancelled' then return; end if;
  if prod.status not in ('draft','open') then raise exception 'cannot cancel a % product', prod.status; end if;
  update products set status = 'cancelled' where id = p_product;
  for r in select * from participations where product_id = p_product loop
    perform _ledger(r.user_id, 'cash', 'refund', r.amount_fils, 'participation', r.id, 'refund:' || r.id);
    update participations set status = 'refunded' where id = r.id;
  end loop;
end $$;
revoke all on function cancel_product from public, anon, authenticated;

-- Member creates a redemption code at a store; balance is held on creation.
create or replace function create_redemption(p_store uuid, p_amount bigint)
returns redemptions
language plpgsql security definer set search_path = public as $$
declare uid uuid := auth.uid(); red redemptions;
begin
  if uid is null then raise exception 'not signed in'; end if;
  if p_amount is null or p_amount <= 0 then raise exception 'amount must be positive'; end if;
  if not exists (select 1 from partner_stores where id = p_store and active) then raise exception 'store not available'; end if;
  insert into redemptions (user_id, store_id, amount_fils, code, expires_at)
  values (uid, p_store, p_amount, upper(encode(gen_random_bytes(5), 'hex')), now() + interval '15 minutes')
  returning * into red;
  begin
    perform _ledger(uid, 'shopping', 'redeem', -p_amount, 'redemption', red.id, 'redeem:' || red.id);
  exception when check_violation then
    raise exception 'insufficient shopping balance';
  end;
  return red;
end $$;
revoke all on function create_redemption from public, anon;
grant execute on function create_redemption to authenticated;

-- Store staff scans the code.
create or replace function confirm_redemption(p_code text) returns redemptions
language plpgsql security definer set search_path = public as $$
declare uid uuid := auth.uid(); red redemptions;
begin
  select * into red from redemptions where code = upper(p_code) for update;
  if not found then raise exception 'code not found'; end if;
  if not exists (select 1 from partner_staff where store_id = red.store_id and user_id = uid) then
    raise exception 'not staff of this store';
  end if;
  if red.status <> 'pending' then raise exception 'code is %', red.status; end if;
  if red.expires_at <= now() then raise exception 'code expired'; end if;
  update redemptions set status = 'confirmed', confirmed_by = uid, confirmed_at = now()
   where id = red.id returning * into red;
  return red;
end $$;
revoke all on function confirm_redemption from public, anon;
grant execute on function confirm_redemption to authenticated;

-- Owner picks where the product goes.
create or replace function set_order_address(p_order uuid, p_address uuid) returns orders
language plpgsql security definer set search_path = public as $$
declare uid uuid := auth.uid(); o orders;
begin
  select * into o from orders where id = p_order and user_id = uid for update;
  if not found then raise exception 'order not found'; end if;
  if o.status not in ('awaiting_address','pending') then raise exception 'order already %', o.status; end if;
  if not exists (select 1 from addresses where id = p_address and user_id = uid) then raise exception 'address not found'; end if;
  update orders set address_id = p_address, status = 'pending' where id = p_order returning * into o;
  return o;
end $$;
revoke all on function set_order_address from public, anon;
grant execute on function set_order_address to authenticated;

-- Job: give back the held balance of codes nobody scanned.
create or replace function expire_redemptions() returns int
language plpgsql security definer set search_path = public as $$
declare r record; n int := 0;
begin
  for r in select * from redemptions where status = 'pending' and expires_at <= now() for update skip locked loop
    update redemptions set status = 'expired' where id = r.id;
    perform _ledger(r.user_id, 'shopping', 'refund', r.amount_fils, 'redemption', r.id, 'unredeem:' || r.id);
    n := n + 1;
  end loop;
  return n;
end $$;
revoke all on function expire_redemptions from public, anon, authenticated;

-- ---------------------------------------------------------------- row level security
-- Reads only. No insert/update/delete policies except where noted, so all
-- money movement goes through the functions above.

alter table profiles enable row level security;
alter table push_tokens enable row level security;
alter table products enable row level security;
alter table product_seeds enable row level security;
alter table wallets enable row level security;
alter table wallet_txns enable row level security;
alter table payments enable row level security;
alter table participations enable row level security;
alter table spots enable row level security;
alter table assignments enable row level security;
alter table addresses enable row level security;
alter table orders enable row level security;
alter table owner_showcase enable row level security;
alter table partner_stores enable row level security;
alter table partner_staff enable row level security;
alter table redemptions enable row level security;
alter table notifications enable row level security;

create policy own_profile_read on profiles for select using (id = auth.uid());
create policy own_profile_edit on profiles for update using (id = auth.uid()) with check (id = auth.uid());
-- members may change only these columns (never is_admin or phone)
revoke update on profiles from anon, authenticated;
grant update (full_name, language) on profiles to authenticated;

create policy own_tokens on push_tokens for all using (user_id = auth.uid()) with check (user_id = auth.uid());

create policy products_public on products for select using (status <> 'draft');

create policy own_wallet on wallets for select using (user_id = auth.uid());
create policy own_txns on wallet_txns for select using (user_id = auth.uid());
create policy own_payments on payments for select using (user_id = auth.uid());
create policy own_participations on participations for select using (user_id = auth.uid());
create policy own_spots on spots for select using (user_id = auth.uid());

-- transparency: anyone can verify an assignment (seed, beacon, winning spot)
create policy assignments_public on assignments for select using (true);

create policy own_addresses on addresses for all using (user_id = auth.uid()) with check (user_id = auth.uid());
create policy own_orders on orders for select using (user_id = auth.uid());

create policy showcase_public on owner_showcase for select using (published_at is not null);

create policy stores_public on partner_stores for select using (active);
create policy own_staff on partner_staff for select using (user_id = auth.uid());

create policy own_redemptions on redemptions for select using (user_id = auth.uid());
create policy staff_redemptions on redemptions for select
  using (exists (select 1 from partner_staff s where s.store_id = redemptions.store_id and s.user_id = auth.uid()));

create policy own_notifications on notifications for select using (user_id = auth.uid());
create policy own_notifications_read on notifications for update using (user_id = auth.uid()) with check (user_id = auth.uid());

-- Admin (is_admin) writes go through the service role in the admin app's
-- server code, never from the browser with the anon key.
