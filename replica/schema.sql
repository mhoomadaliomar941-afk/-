-- Local on-device database (SQLite via expo-sqlite). Migration 001.
-- No server and no accounts, so there is no user_id column and no row level
-- security: the database file belongs to one install. Times are ISO-8601 UTC text.
-- Level content is not stored here: levels ship as JSON files in the app bundle.

pragma foreign_keys = on;

create table level_progress (
  level_id       text primary key,              -- stable id from the level file, never its position
  status         text not null default 'locked'
                 check (status in ('locked','current','solved','skipped')),
  hints_revealed integer not null default 0 check (hints_revealed >= 0),
  attempts       integer not null default 0 check (attempts >= 0),
  solved_at      text,
  updated_at     text not null default (strftime('%Y-%m-%dT%H:%M:%fZ','now'))
);
create index level_progress_status on level_progress (status);

-- The wallet is a ledger. Balance = sum(amount). Never store a balance you can drift from.
create table wallet_ledger (
  id         integer primary key autoincrement,
  kind       text not null
             check (kind in ('starting_grant','daily_gift','rewarded_ad','purchase',
                             'subscription_grant','hint','skip')),
  amount     integer not null check (amount <> 0),   -- positive = earned, negative = spent
  level_id   text references level_progress(level_id) on delete set null,
  -- store transaction id, ad reward id, or gift date: makes every grant apply exactly once
  dedupe_key text unique,
  created_at text not null default (strftime('%Y-%m-%dT%H:%M:%fZ','now'))
);
create index wallet_ledger_kind on wallet_ledger (kind, created_at);

-- Cached copy of what the store says the player owns. The store is the source of truth;
-- this row only lets "no ads" hold while offline.
create table entitlements (
  key        text primary key check (key in ('no_ads','subscription')),
  active     integer not null default 0 check (active in (0,1)),
  expires_at text,                                   -- null for one-time purchases
  checked_at text not null
);

create table daily_gift (
  id              integer primary key check (id = 1),  -- single row
  last_claim_day  text,                                -- local calendar day, YYYY-MM-DD
  last_claim_at   text,                                -- UTC instant of that claim
  streak          integer not null default 0 check (streak >= 0)
);
insert into daily_gift (id) values (1);

create table settings (
  key   text primary key
        check (key in ('sound','music','vibration','language','ads_consent','levels_since_ad')),
  value text not null
);

-- Hard constraints the recon found:
--  * a hint or skip must never be charged twice for the same level step
create unique index one_charge_per_hint
  on wallet_ledger (level_id, kind, dedupe_key) where kind in ('hint','skip');
--  * only one level is "current" at a time
create unique index one_current_level
  on level_progress (status) where status = 'current';
