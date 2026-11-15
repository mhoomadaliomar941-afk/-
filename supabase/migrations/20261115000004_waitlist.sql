-- Pre-launch waitlist from the landing page. Anyone can add a number (with
-- consent); nobody but the server can read the list.
create table waitlist (
  id uuid primary key default gen_random_uuid(),
  phone text not null unique check (phone ~ '^\+9627[789][0-9]{7}$'),
  consent_at timestamptz not null,
  created_at timestamptz not null default now()
);
alter table waitlist enable row level security;
create policy waitlist_join on waitlist for insert to anon, authenticated
  with check (consent_at is not null and consent_at <= now() + interval '5 minutes');
revoke all on waitlist from anon, authenticated;
grant insert (phone, consent_at) on waitlist to anon, authenticated;
