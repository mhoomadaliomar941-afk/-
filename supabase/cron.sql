-- Run ONCE in the Supabase SQL editor after deploying the functions.
-- 1) Store two secrets in Vault (values are yours, not in git):
--      select vault.create_secret('https://<project-ref>.supabase.co', 'project_url');
--      select vault.create_secret('<the same value as the CRON_SECRET function secret>', 'cron_secret');
-- 2) Then run the rest of this file.
create extension if not exists pg_cron;
create extension if not exists pg_net;

create or replace function public.call_job(task text) returns bigint
language sql security definer set search_path = public as $$
  select net.http_post(
    url := (select decrypted_secret from vault.decrypted_secrets where name = 'project_url') || '/functions/v1/jobs?task=' || task,
    headers := jsonb_build_object('Content-Type', 'application/json',
      'x-cron-secret', (select decrypted_secret from vault.decrypted_secrets where name = 'cron_secret')),
    body := '{}'::jsonb, timeout_milliseconds := 25000)
$$;
revoke all on function public.call_job(text) from public, anon, authenticated;

select cron.schedule('assign-owners',      '* * * * *',   $$ select public.call_job('assign-owners') $$);
select cron.schedule('push',               '* * * * *',   $$ select public.call_job('push') $$);
select cron.schedule('expire-redemptions', '* * * * *',   $$ select public.call_job('expire-redemptions') $$);
select cron.schedule('cancel-expired',     '*/5 * * * *', $$ select public.call_job('cancel-expired') $$);
select cron.schedule('reconcile-payments', '17 * * * *',  $$ select public.call_job('reconcile-payments') $$);
-- Failed calls: select * from net._http_response where status_code >= 400 order by created desc;
