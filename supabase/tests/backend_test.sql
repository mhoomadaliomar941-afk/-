-- Run: supabase/tests/run.sh  (plain Postgres + stub_supabase.sql + all migrations + this file)
-- Every check raises on failure; the last line prints ALL PASSED.
\set ON_ERROR_STOP 1
set client_min_messages = warning;

create function pg_temp.expect_error(p_sql text, p_pattern text) returns void language plpgsql as $$
begin
  execute p_sql;
  raise exception 'expected error matching "%" from: %', p_pattern, p_sql;
exception when others then
  if sqlerrm like 'expected error matching%' then raise; end if;
  if sqlerrm !~ p_pattern then raise exception 'wrong error for %: got "%", wanted "%"', p_sql, sqlerrm, p_pattern; end if;
end $$;
create function pg_temp.check(ok boolean, what text) returns void language plpgsql as $$
begin if not coalesce(ok, false) then raise exception 'FAILED: %', what; end if; raise notice 'ok: %', what; end $$;
grant execute on function pg_temp.expect_error(text, text), pg_temp.check(boolean, text) to authenticated, service_role, anon;
set client_min_messages = notice;

-- users A, B, C sign up by phone: trigger creates profile + wallet
insert into auth.users values ('00000000-0000-0000-0000-00000000000a', '962790000001'),
  ('00000000-0000-0000-0000-00000000000b', '962790000002'), ('00000000-0000-0000-0000-00000000000c', '962790000003');
select pg_temp.check((select count(*) from profiles) = 3 and (select count(*) from wallets) = 3, 'sign-up creates profile and wallet');
select pg_temp.check((select phone from profiles where id = '00000000-0000-0000-0000-00000000000a') = '+962790000001', 'phone stored as E.164');

-- each tops up 5 JD (webhook path, twice = idempotent)
insert into payments (user_id, gateway, amount_fils, external_ref) select id, 'hyperpay', 5000, 'chk-' || phone from profiles;
select confirm_topup(id) from payments; select confirm_topup(id) from payments;
select pg_temp.check((select sum(cash_fils) from wallets) = 15000, 'double webhook credits once');

-- admin creates a draft and opens it: seed generated, hash published
insert into products (id, title_ar, title_en, retail_price_fils, spot_price_fils, total_spots, category)
values ('11111111-1111-1111-1111-111111111111', 'هاتف', 'Phone', 800000, 1000, 4, 'phone');
select open_product('11111111-1111-1111-1111-111111111111');
select pg_temp.check((select status = 'open' and seed_hash ~ '^[0-9a-f]{64}$' from products), 'open_product publishes the seed hash');
select pg_temp.expect_error($$select open_product('11111111-1111-1111-1111-111111111111')$$, 'not draft');

-- buying as members
set role authenticated;
set request.jwt.uid = '00000000-0000-0000-0000-00000000000a';
select spots from buy_spots('11111111-1111-1111-1111-111111111111', 2, 'ka');
select pg_temp.check((select array_length(spot_numbers, 1) from my_participations) = 2, 'my_participations lists spot numbers');
select pg_temp.expect_error($$select * from product_seeds$$, 'permission denied');
select pg_temp.expect_error($$select assign_owner('11111111-1111-1111-1111-111111111111', 1, repeat('a', 64))$$, 'permission denied');
select pg_temp.expect_error($$select open_product('11111111-1111-1111-1111-111111111111')$$, 'permission denied');

set request.jwt.uid = '00000000-0000-0000-0000-00000000000b';
-- second user sees nothing of A's
select pg_temp.check((select count(*) from wallets) = 1 and (select count(*) from wallet_txns where user_id <> auth.uid()) = 0, 'B sees only own wallet and ledger');
select pg_temp.check((select count(*) from participations) = 0 and (select count(*) from my_participations) = 0 and (select count(*) from spots) = 0, 'B sees none of A''s participations or spots');
select pg_temp.check((select count(*) from payments) = 1, 'B sees only own payment');
select pg_temp.check((select count(*) from profiles) = 1, 'B sees only own profile');
select pg_temp.expect_error($$update profiles set is_admin = true$$, 'permission denied');
select pg_temp.expect_error($$update wallets set cash_fils = 999999$$, 'permission denied|row-level');
select pg_temp.expect_error($$insert into wallet_txns (user_id, bucket, type, amount_fils) values (auth.uid(), 'cash', 'topup', 100000)$$, 'permission denied|row-level');
select spots from buy_spots('11111111-1111-1111-1111-111111111111', 1, 'kb');
set request.jwt.uid = '00000000-0000-0000-0000-00000000000c';
select spots from buy_spots('11111111-1111-1111-1111-111111111111', 1, 'kc');
reset role;

select pg_temp.check((select status = 'full' and filled_at is not null from products), 'last spot marks full and stamps filled_at');

-- fairness: only the first beacon round after filling is accepted
select pg_temp.expect_error(format($$select assign_owner('11111111-1111-1111-1111-111111111111', %s, repeat('a', 64))$$,
  (select drand_round_after(filled_at) - 1 from products)), 'beacon round must be');
select pg_temp.expect_error(format($$select assign_owner('11111111-1111-1111-1111-111111111111', %s, repeat('a', 64))$$,
  (select drand_round_after(filled_at) + 1 from products)), 'beacon round must be');
select pg_temp.expect_error(format($$select assign_owner('11111111-1111-1111-1111-111111111111', %s, 'nothex')$$,
  (select drand_round_after(filled_at) from products)), '64 hex');
select winning_spot from assign_owner('11111111-1111-1111-1111-111111111111',
  (select drand_round_after(filled_at) from products), encode(digest('drand-test', 'sha256'), 'hex'));
select pg_temp.check((select status = 'assigned' from products), 'assigned with the right round');
select pg_temp.check((select count(*) from notifications where type in ('owner','credited')) = 3, 'one result notification per member');
select pg_temp.check((select sum(shopping_fils) from wallets) = 4000 - (select sum(amount_fils) from participations where status = 'owner'),
  'everyone but the owner credited as shopping balance');

-- public view: winning spot and proof visible, owner id hidden
set role authenticated;
set request.jwt.uid = '00000000-0000-0000-0000-00000000000b';
select pg_temp.check((select count(*) from public_assignments) = 1, 'B can verify the result');
select pg_temp.expect_error($$select owner_id from assignments where owner_id <> auth.uid()$$, 'permission denied');
select pg_temp.check((select is_mine = (select status = 'owner' from my_participations limit 1) from public_assignments), 'is_mine matches B''s own outcome');
reset role;
set role anon;
select pg_temp.expect_error($$select * from public_assignments$$, 'permission denied');
select pg_temp.check((select count(*) from products) = 1, 'anon can browse products');
reset role;

-- account deletion
set role authenticated;
set request.jwt.uid = '00000000-0000-0000-0000-00000000000b';
select pg_temp.expect_error($$select request_account_deletion()$$, 'balance_not_zero');
reset role;
update wallets set cash_fils = 0, shopping_fils = 0 where user_id = '00000000-0000-0000-0000-00000000000b';
set role authenticated;
set request.jwt.uid = '00000000-0000-0000-0000-00000000000b';
select request_account_deletion();
reset role;
select pg_temp.check((select phone like 'deleted:%' and full_name is null from profiles where id = '00000000-0000-0000-0000-00000000000b'), 'deletion removes phone and name');
select pg_temp.check((select count(*) from notifications where user_id = '00000000-0000-0000-0000-00000000000b') = 0, 'deletion removes notifications');
select pg_temp.check((select count(*) from wallet_txns where user_id = '00000000-0000-0000-0000-00000000000b') > 0, 'ledger kept for money records');

-- ledger = balance for every member whose balance only moved through the ledger (B was zeroed by hand above)
select pg_temp.check(not exists (
  select 1 from wallets w
  where w.user_id <> '00000000-0000-0000-0000-00000000000b'
    and w.cash_fils + w.shopping_fils <> (select coalesce(sum(amount_fils), 0) from wallet_txns t where t.user_id = w.user_id)
), 'ledger sums equal wallet balances');
-- the Edge Functions' role can run the server-only functions
set role service_role;
select pg_temp.check((select count(*) from expire_redemptions()) = 1, 'service_role can run jobs');
select pg_temp.check(has_function_privilege('service_role', 'confirm_topup(uuid)', 'execute')
  and has_function_privilege('service_role', 'assign_owner(uuid,bigint,text)', 'execute')
  and not has_function_privilege('authenticated', 'confirm_topup(uuid)', 'execute'), 'money functions: service_role yes, members no');
reset role;
\echo ALL PASSED
