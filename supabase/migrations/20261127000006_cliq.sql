-- Top-ups by CliQ transfer, confirmed by hand by the owner (no gateway needed).
-- The member gets the owner's CliQ alias and a unique reference, transfers from
-- their own bank app, taps "sent"; the owner checks their bank app and approves.
-- Approving credits through confirm_topup(), so a double tap credits once.

-- ---------------------------------------------------------------- settings the admin edits

create table app_settings (
  key text primary key check (key in ('cliq_alias', 'cliq_holder', 'cliq_hours', 'card_enabled')),
  value text not null default '',
  updated_at timestamptz not null default now()
);
insert into app_settings (key, value) values ('cliq_alias', ''), ('cliq_holder', ''), ('cliq_hours', 'من 9 الصبح لـ 11 بالليل'), ('card_enabled', 'false');
alter table app_settings enable row level security;
create policy settings_read on app_settings for select to authenticated using (true);
revoke insert, update, delete on app_settings from anon, authenticated;
revoke all on app_settings from anon;

create or replace function admin_save_settings(p jsonb) returns void
language plpgsql security definer set search_path = public as $$
declare k text;
begin
  if not is_admin() then raise exception 'admins only'; end if;
  for k in select jsonb_object_keys(p) loop
    if k not in ('cliq_alias', 'cliq_holder', 'cliq_hours', 'card_enabled') then raise exception 'unknown setting %', k; end if;
    if length(p->>k) > 120 then raise exception 'setting too long'; end if;
    update app_settings set value = trim(p->>k), updated_at = now() where key = k;
  end loop;
end $$;

-- ---------------------------------------------------------------- CliQ requests live in payments

alter table payments add column claimed_at timestamptz;          -- member tapped "sent"
alter table payments add column reviewed_by uuid references profiles(id) on delete set null;
alter table payments add column reviewed_at timestamptz;
alter table payments add column reject_reason text;
create index on payments (gateway, status, claimed_at);

create policy admin_payments on payments for select using (is_admin());

-- notifications for the top-up outcome
-- (notifications.type is free text; the app and the push job know these two)

-- Member: start a CliQ top-up. Returns the payment with its reference (external_ref).
create or replace function cliq_start_topup(p_amount bigint) returns payments
language plpgsql security definer set search_path = public as $$
declare uid uuid := auth.uid(); pay payments; ref text; alias text;
begin
  if uid is null then raise exception 'not signed in'; end if;
  if p_amount is null or p_amount < 1000 or p_amount > 500000 or p_amount % 1000 <> 0 then
    raise exception 'amount must be whole dinars between 1 and 500';
  end if;
  select value into alias from app_settings where key = 'cliq_alias';
  if coalesce(alias, '') = '' then raise exception 'cliq not available'; end if;
  if (select count(*) from payments where user_id = uid and gateway = 'cliq' and status = 'pending') >= 3 then
    raise exception 'too many pending top-ups';
  end if;
  loop
    ref := 'NZ-' || lpad((floor(random() * 1000000))::int::text, 6, '0');
    exit when not exists (select 1 from payments where external_ref = ref);
  end loop;
  insert into payments (user_id, gateway, amount_fils, external_ref) values (uid, 'cliq', p_amount, ref) returning * into pay;
  return pay;
end $$;

-- Member: "I've transferred it."
create or replace function cliq_mark_sent(p_payment uuid) returns payments
language plpgsql security definer set search_path = public as $$
declare pay payments;
begin
  update payments set claimed_at = coalesce(claimed_at, now())
   where id = p_payment and user_id = auth.uid() and gateway = 'cliq' and status = 'pending'
  returning * into pay;
  if not found then raise exception 'payment not found'; end if;
  return pay;
end $$;

-- Admin: approve (credits once, via confirm_topup) or reject with a reason.
create or replace function admin_review_cliq(p_payment uuid, p_approve boolean, p_reason text default null) returns payments
language plpgsql security definer set search_path = public as $$
declare pay payments;
begin
  if not is_admin() then raise exception 'admins only'; end if;
  select * into pay from payments where id = p_payment and gateway = 'cliq' for update;
  if not found then raise exception 'payment not found'; end if;
  if pay.status <> 'pending' then return pay; end if;              -- already decided: no double credit
  if p_approve then
    perform confirm_topup(p_payment);
    insert into notifications (user_id, type, payload)
    values (pay.user_id, 'topup_ok', jsonb_build_object('amount_fils', pay.amount_fils, 'reference', pay.external_ref));
  else
    update payments set status = 'failed', reject_reason = nullif(trim(coalesce(p_reason, '')), '') where id = p_payment;
    insert into notifications (user_id, type, payload)
    values (pay.user_id, 'topup_rejected', jsonb_build_object('amount_fils', pay.amount_fils, 'reference', pay.external_ref, 'reason', p_reason));
  end if;
  update payments set reviewed_by = auth.uid(), reviewed_at = now() where id = p_payment returning * into pay;
  return pay;
end $$;

-- Job: CliQ requests nobody marked as sent within a day are dropped (no money moved).
create or replace function expire_cliq_requests() returns int
language sql security definer set search_path = public as $$
  with x as (
    update payments set status = 'failed', reject_reason = 'expired'
     where gateway = 'cliq' and status = 'pending' and claimed_at is null and created_at < now() - interval '24 hours'
    returning 1
  ) select count(*)::int from x
$$;

revoke all on function admin_save_settings, cliq_start_topup, cliq_mark_sent, admin_review_cliq from public, anon;
grant execute on function admin_save_settings, cliq_start_topup, cliq_mark_sent, admin_review_cliq to authenticated;
revoke all on function expire_cliq_requests from public, anon, authenticated;
grant execute on function expire_cliq_requests to service_role;
