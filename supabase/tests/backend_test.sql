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

-- account deletion (by a member who was not picked: the owner has an open delivery and is refused, by design)
select user_id as del_uid from participations where status = 'credited' limit 1 \gset
set role authenticated;
set request.jwt.uid = :'del_uid';
select pg_temp.expect_error($$select request_account_deletion()$$, 'balance_not_zero');
reset role;
update wallets set cash_fils = 0, shopping_fils = 0 where user_id = :'del_uid';
set role authenticated;
set request.jwt.uid = :'del_uid';
select request_account_deletion();
reset role;
select pg_temp.check((select phone like 'deleted:%' and full_name is null from profiles where id = :'del_uid'), 'deletion removes phone and name');
select pg_temp.check((select count(*) from notifications where user_id = :'del_uid') = 0, 'deletion removes notifications');
select pg_temp.check((select count(*) from wallet_txns where user_id = :'del_uid') > 0, 'ledger kept for money records');

-- ledger = balance for every member whose balance only moved through the ledger (the deleted member was zeroed by hand above)
select pg_temp.check(not exists (
  select 1 from wallets w
  where w.user_id <> :'del_uid'
    and w.cash_fils + w.shopping_fils <> (select coalesce(sum(amount_fils), 0) from wallet_txns t where t.user_id = w.user_id)
), 'ledger sums equal wallet balances');
-- the Edge Functions' role can run the server-only functions
set role service_role;
select pg_temp.check((select count(*) from expire_redemptions()) = 1, 'service_role can run jobs');
select pg_temp.check(has_function_privilege('service_role', 'confirm_topup(uuid)', 'execute')
  and has_function_privilege('service_role', 'assign_owner(uuid,bigint,text)', 'execute')
  and not has_function_privilege('authenticated', 'confirm_topup(uuid)', 'execute'), 'money functions: service_role yes, members no');
reset role;
-- waitlist: anyone can join with consent, nobody can read it
set role anon;
insert into waitlist (phone, consent_at) values ('+962791112233', now());
select pg_temp.expect_error($$insert into waitlist (phone, consent_at) values ('12345', now())$$, 'check constraint');
select pg_temp.expect_error($$insert into waitlist (phone, consent_at) values ('+962791112244', null)$$, 'not-null|row-level');
select pg_temp.expect_error($$select * from waitlist$$, 'permission denied');
reset role;
select pg_temp.check((select count(*) from waitlist) = 1, 'waitlist: anon joins with consent, cannot read');
-- ---------------------------------------------------------------- store + admin
update profiles set is_admin = true where id = '00000000-0000-0000-0000-00000000000a';
set role authenticated;
-- a non-admin cannot do admin things
set request.jwt.uid = '00000000-0000-0000-0000-00000000000c';
select pg_temp.expect_error($$select admin_save_store_product('{"title_ar":"x","price_fils":1000,"stock":1}')$$, 'admins only');
select pg_temp.expect_error($$select admin_save_product('{"title_ar":"x","retail_price_fils":1000,"spot_price_fils":1000,"total_spots":2}')$$, 'admins only');
select pg_temp.expect_error($$insert into store_products (title_ar, price_fils) values ('x', 1)$$, 'permission denied');
-- the admin creates a store product and a draw product, then opens the draw
set request.jwt.uid = '00000000-0000-0000-0000-00000000000a';
select id as sp_id from admin_save_store_product('{"title_ar":"شاحن سريع","price_fils":4000,"stock":3,"category":"other"}') \gset
select id as dp_id from admin_save_product('{"title_ar":"سماعات","retail_price_fils":150000,"spot_price_fils":1000,"total_spots":10,"category":"audio"}') \gset
select pg_temp.check((select status = 'draft' from products where id = :'dp_id'), 'admin creates a draft draw product');
select pg_temp.check((select status = 'open' and seed_hash is not null from admin_open_product(:'dp_id')), 'admin opens it (seed hash published)');
select pg_temp.expect_error(format($$select admin_save_product('{"id":"%s","title_ar":"x","retail_price_fils":1,"spot_price_fils":1000,"total_spots":5}')$$, :'dp_id'), 'only drafts');
-- C buys from the store: shopping first, then cash; stock goes down; ledger has both
set request.jwt.uid = '00000000-0000-0000-0000-00000000000c';
insert into addresses (user_id, governorate, city, street, phone) values (auth.uid(), 'عمّان', 'عمّان', 'شارع ١', '0791234567');
reset role;
update wallets set shopping_fils = 3000, cash_fils = 5000 where user_id = '00000000-0000-0000-0000-00000000000c';  -- known start (the deletion test above may have zeroed C)
insert into wallet_txns (user_id, bucket, type, amount_fils, idempotency_key) values ('00000000-0000-0000-0000-00000000000c', 'shopping', 'adjust', 3000, 'test-adjust-c');
select cash_fils as c_cash0 from wallets where user_id = '00000000-0000-0000-0000-00000000000c' \gset
set role authenticated;
set request.jwt.uid = '00000000-0000-0000-0000-00000000000c';
select pg_temp.expect_error(format($$select checkout_store('[{"product_id":"%s","qty":4}]', (select id from addresses limit 1), 'k-big')$$, :'sp_id'), 'only 3 left');
select total_fils, paid_shopping_fils, paid_cash_fils from checkout_store(format('[{"product_id":"%s","qty":1}]', :'sp_id')::jsonb, (select id from addresses limit 1), 'k-store-1');
select checkout_store(format('[{"product_id":"%s","qty":1}]', :'sp_id')::jsonb, (select id from addresses limit 1), 'k-store-1');  -- retry: no second charge
reset role;
select pg_temp.check((select paid_shopping_fils = 3000 and paid_cash_fils = 1000 from store_orders where idempotency_key = 'k-store-1'), 'store: shopping balance first, then cash');
select pg_temp.check((select count(*) from store_orders where user_id = '00000000-0000-0000-0000-00000000000c') = 1, 'store: retry does not double-charge');
select pg_temp.check((select stock from store_products where id = :'sp_id') = 2, 'store: stock went down by one');
select pg_temp.check((select shopping_fils = 0 and cash_fils = :c_cash0 - 1000 from wallets where user_id = '00000000-0000-0000-0000-00000000000c'), 'store: balances charged correctly');
-- other members can't see C's order; the admin can, and cancelling refunds money and stock
set role authenticated;
set request.jwt.uid = '00000000-0000-0000-0000-00000000000b';
select pg_temp.check((select count(*) from store_orders) = 0, 'store: other members see no one else''s orders');
set request.jwt.uid = '00000000-0000-0000-0000-00000000000a';
select pg_temp.check((select count(*) from store_orders) = 1, 'store: admin sees all orders');
select status from admin_set_store_order_status((select id from store_orders limit 1), 'cancelled');
reset role;
select pg_temp.check((select stock from store_products where id = :'sp_id') = 3, 'cancel: stock returned');
select pg_temp.check((select shopping_fils = 3000 and cash_fils = :c_cash0 from wallets where user_id = '00000000-0000-0000-0000-00000000000c'), 'cancel: money returned to the same balances');
-- ---------------------------------------------------------------- CliQ top-ups (owner confirms by hand)
set role authenticated;
set request.jwt.uid = '00000000-0000-0000-0000-00000000000c';
select pg_temp.expect_error($$select cliq_start_topup(5000)$$, 'cliq not available');               -- no alias set yet
select pg_temp.expect_error($$select admin_save_settings('{"cliq_alias":"X"}')$$, 'admins only');
set request.jwt.uid = '00000000-0000-0000-0000-00000000000a';
select admin_save_settings('{"cliq_alias":"NAZIH","cliq_holder":"Test Owner"}');
select pg_temp.expect_error($$select admin_save_settings('{"secret":"x"}')$$, 'unknown setting');
set request.jwt.uid = '00000000-0000-0000-0000-00000000000c';
select pg_temp.expect_error($$select cliq_start_topup(1500)$$, 'whole dinars');
select id as q1, external_ref as q1ref from cliq_start_topup(10000) \gset
select pg_temp.check(:'q1ref' ~ '^NZ-[0-9]{6}$', 'cliq: reference like NZ-123456');
select pg_temp.check((select claimed_at is not null from cliq_mark_sent(:'q1')), 'cliq: member marks it sent');
select pg_temp.expect_error(format($$select admin_review_cliq('%s', true)$$, :'q1'), 'admins only');
reset role;
select cash_fils as c_before from wallets where user_id = '00000000-0000-0000-0000-00000000000c' \gset
set role authenticated;
set request.jwt.uid = '00000000-0000-0000-0000-00000000000a';
select pg_temp.check((select count(*) from payments where gateway = 'cliq' and claimed_at is not null) = 1, 'cliq: admin sees the claimed request');
select status from admin_review_cliq(:'q1', true);
select status from admin_review_cliq(:'q1', true);   -- double tap
reset role;
select pg_temp.check((select cash_fils from wallets where user_id = '00000000-0000-0000-0000-00000000000c') = :c_before + 10000, 'cliq: approve credits once, even on a double tap');
select pg_temp.check((select count(*) from notifications where user_id = '00000000-0000-0000-0000-00000000000c' and type = 'topup_ok') = 1, 'cliq: member notified once');
-- reject path
set role authenticated;
set request.jwt.uid = '00000000-0000-0000-0000-00000000000c';
select id as q2 from cliq_start_topup(2000) \gset
set request.jwt.uid = '00000000-0000-0000-0000-00000000000a';
select status from admin_review_cliq(:'q2', false, 'ما وصل تحويل بهالرقم');
reset role;
select pg_temp.check((select status = 'failed' and reject_reason is not null from payments where id = :'q2'), 'cliq: reject keeps the reason');
select pg_temp.check((select cash_fils from wallets where user_id = '00000000-0000-0000-0000-00000000000c') = :c_before + 10000, 'cliq: reject credits nothing');
-- unclaimed requests expire after a day
set role authenticated;
set request.jwt.uid = '00000000-0000-0000-0000-00000000000c';
select id as q3 from cliq_start_topup(3000) \gset
reset role;
update payments set created_at = now() - interval '25 hours' where id = :'q3';
set role service_role;
select pg_temp.check((select expire_cliq_requests()) = 1, 'cliq: unclaimed request expires after 24h');
reset role;
\echo ALL PASSED
