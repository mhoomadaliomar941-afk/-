-- Live updates for the app (balance after a top-up, results, new notifications).
-- Realtime applies RLS, so members only receive their own rows.
do $$ begin
  if exists (select 1 from pg_publication where pubname = 'supabase_realtime') then
    alter publication supabase_realtime add table wallets, notifications, products, orders;
  end if;
end $$;
