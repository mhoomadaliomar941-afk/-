-- The owner's own store (shopping balance is spent here) and the admin panel.
-- Admins are profiles with is_admin = true. Make the owner an admin once, in the
-- SQL editor:  update profiles set is_admin = true where phone = '+9627XXXXXXXX';
-- Every admin action is a security-definer function that checks is_admin().

create or replace function is_admin() returns boolean
language sql stable security definer set search_path = public as $$
  select coalesce((select is_admin from profiles where id = auth.uid()), false)
$$;
grant execute on function is_admin() to authenticated;

-- ---------------------------------------------------------------- store catalogue

create table store_products (
  id uuid primary key default gen_random_uuid(),
  title_ar text not null check (length(title_ar) between 1 and 120),
  description_ar text not null default '',
  category text not null default 'phone'
    check (category in ('phone','console','watch','audio','laptop','tablet','home','beauty','other')),
  price_fils bigint not null check (price_fils > 0),
  stock int not null default 0 check (stock >= 0),
  images text[] not null default '{}',
  active boolean not null default true,
  created_at timestamptz not null default now(),
  updated_at timestamptz not null default now()
);
create index on store_products (active, created_at desc);
create trigger t_store_products_upd before update on store_products for each row execute function set_updated_at();

create table store_orders (
  id uuid primary key default gen_random_uuid(),
  user_id uuid not null references profiles(id) on delete restrict,
  address_id uuid not null references addresses(id) on delete restrict,
  total_fils bigint not null check (total_fils > 0),
  paid_shopping_fils bigint not null check (paid_shopping_fils >= 0),
  paid_cash_fils bigint not null check (paid_cash_fils >= 0),
  status text not null default 'pending' check (status in ('pending','shipped','delivered','cancelled')),
  idempotency_key text not null unique,
  created_at timestamptz not null default now(),
  updated_at timestamptz not null default now(),
  constraint paid_in_full check (paid_shopping_fils + paid_cash_fils = total_fils)
);
create index on store_orders (user_id, created_at desc);
create index on store_orders (status, created_at desc);
create trigger t_store_orders_upd before update on store_orders for each row execute function set_updated_at();

create table store_order_items (
  order_id uuid not null references store_orders(id) on delete cascade,
  product_id uuid not null references store_products(id) on delete restrict,
  title_ar text not null,                -- what it was called when bought
  qty int not null check (qty between 1 and 20),
  unit_price_fils bigint not null check (unit_price_fils > 0),
  primary key (order_id, product_id)
);
create index on store_order_items (product_id);

-- ledger gets a 'purchase' type for store spending
alter table wallet_txns drop constraint wallet_txns_type_check;
alter table wallet_txns add constraint wallet_txns_type_check
  check (type in ('topup','spend','credit','redeem','refund','adjust','purchase'));

-- ---------------------------------------------------------------- access

alter table store_products enable row level security;
alter table store_orders enable row level security;
alter table store_order_items enable row level security;

create policy store_public on store_products for select using (active or is_admin());
create policy own_store_orders on store_orders for select using (user_id = auth.uid() or is_admin());
create policy own_store_items on store_order_items for select
  using (exists (select 1 from store_orders o where o.id = order_id and (o.user_id = auth.uid() or is_admin())));

-- admins see everything they need to run the shop and ship orders
create policy admin_products on products for select using (is_admin());
create policy admin_orders on orders for select using (is_admin());
create policy admin_addresses on addresses for select using (is_admin());
create policy admin_profiles on profiles for select using (is_admin());
create policy admin_participations on participations for select using (is_admin());

revoke insert, update, delete on store_products, store_orders, store_order_items from anon, authenticated;
revoke all on store_orders, store_order_items from anon;

-- ---------------------------------------------------------------- checkout (members)

-- p_items: [{"product_id": "...", "qty": 2}, ...]. Shopping balance is used first,
-- then cash balance for the rest. Locks the products so stock never goes negative.
create or replace function checkout_store(p_items jsonb, p_address uuid, p_key text) returns store_orders
language plpgsql security definer set search_path = public as $$
declare
  uid uuid := auth.uid();
  o store_orders;
  it record;
  sp store_products;
  total bigint := 0;
  w wallets;
  from_shopping bigint;
begin
  if uid is null then raise exception 'not signed in'; end if;
  select * into o from store_orders where idempotency_key = p_key;
  if found then
    if o.user_id <> uid then raise exception 'idempotency key in use'; end if;
    return o;
  end if;
  if jsonb_typeof(p_items) <> 'array' or jsonb_array_length(p_items) = 0 then raise exception 'cart is empty'; end if;
  if not exists (select 1 from addresses where id = p_address and user_id = uid) then raise exception 'address not found'; end if;

  -- price and stock, with the rows locked
  for it in select (e->>'product_id')::uuid as pid, (e->>'qty')::int as qty from jsonb_array_elements(p_items) e loop
    if it.qty is null or it.qty < 1 or it.qty > 20 then raise exception 'qty must be 1 to 20'; end if;
    select * into sp from store_products where id = it.pid for update;
    if not found or not sp.active then raise exception 'product not available'; end if;
    if sp.stock < it.qty then raise exception 'only % left', sp.stock; end if;
    total := total + sp.price_fils * it.qty;
  end loop;

  select * into w from wallets where user_id = uid for update;
  if coalesce(w.shopping_fils, 0) + coalesce(w.cash_fils, 0) < total then raise exception 'insufficient balance'; end if;
  from_shopping := least(coalesce(w.shopping_fils, 0), total);

  insert into store_orders (user_id, address_id, total_fils, paid_shopping_fils, paid_cash_fils, idempotency_key)
  values (uid, p_address, total, from_shopping, total - from_shopping, p_key) returning * into o;

  for it in select (e->>'product_id')::uuid as pid, (e->>'qty')::int as qty from jsonb_array_elements(p_items) e loop
    update store_products set stock = stock - it.qty where id = it.pid returning * into sp;
    insert into store_order_items (order_id, product_id, title_ar, qty, unit_price_fils)
    values (o.id, it.pid, sp.title_ar, it.qty, sp.price_fils);
  end loop;

  if from_shopping > 0 then
    perform _ledger(uid, 'shopping', 'purchase', -from_shopping, 'store_order', o.id, 'store-shop:' || o.id);
  end if;
  if total - from_shopping > 0 then
    perform _ledger(uid, 'cash', 'purchase', -(total - from_shopping), 'store_order', o.id, 'store-cash:' || o.id);
  end if;
  return o;
end $$;
revoke all on function checkout_store from public, anon;
grant execute on function checkout_store to authenticated;

-- ---------------------------------------------------------------- admin: draw products

-- Create or edit a draft. Once a product is open, its price and spots are locked
-- (members have paid on those terms); only the deadline can still move later.
create or replace function admin_save_product(p jsonb) returns products
language plpgsql security definer set search_path = public as $$
declare r products; pid uuid := nullif(p->>'id', '')::uuid;
begin
  if not is_admin() then raise exception 'admins only'; end if;
  if pid is null then
    insert into products (title_ar, title_en, category, description_ar, specs, images, retail_price_fils, spot_price_fils, total_spots, max_spots_per_user, closes_at)
    values (p->>'title_ar', coalesce(nullif(p->>'title_en', ''), p->>'title_ar'), coalesce(p->>'category', 'phone'), coalesce(p->>'description_ar', ''),
            coalesce(p->'specs', '{}'::jsonb), coalesce(array(select jsonb_array_elements_text(p->'images')), '{}'),
            (p->>'retail_price_fils')::bigint, (p->>'spot_price_fils')::bigint, (p->>'total_spots')::int,
            nullif(p->>'max_spots_per_user', '')::int, nullif(p->>'closes_at', '')::timestamptz)
    returning * into r;
  else
    select * into r from products where id = pid for update;
    if not found then raise exception 'product not found'; end if;
    if r.status <> 'draft' then raise exception 'only drafts can be edited'; end if;
    update products set
      title_ar = p->>'title_ar', title_en = coalesce(nullif(p->>'title_en', ''), p->>'title_ar'),
      category = coalesce(p->>'category', category), description_ar = coalesce(p->>'description_ar', ''),
      specs = coalesce(p->'specs', specs), images = coalesce(array(select jsonb_array_elements_text(p->'images')), images),
      retail_price_fils = (p->>'retail_price_fils')::bigint, spot_price_fils = (p->>'spot_price_fils')::bigint,
      total_spots = (p->>'total_spots')::int, max_spots_per_user = nullif(p->>'max_spots_per_user', '')::int,
      closes_at = nullif(p->>'closes_at', '')::timestamptz
    where id = pid returning * into r;
  end if;
  return r;
end $$;

create or replace function admin_open_product(p_product uuid) returns products
language plpgsql security definer set search_path = public as $$
begin
  if not is_admin() then raise exception 'admins only'; end if;
  return open_product(p_product);
end $$;

create or replace function admin_cancel_product(p_product uuid) returns void
language plpgsql security definer set search_path = public as $$
begin
  if not is_admin() then raise exception 'admins only'; end if;
  perform cancel_product(p_product);
end $$;

-- ---------------------------------------------------------------- admin: store products

create or replace function admin_save_store_product(p jsonb) returns store_products
language plpgsql security definer set search_path = public as $$
declare r store_products; pid uuid := nullif(p->>'id', '')::uuid;
begin
  if not is_admin() then raise exception 'admins only'; end if;
  if pid is null then
    insert into store_products (title_ar, description_ar, category, price_fils, stock, images, active)
    values (p->>'title_ar', coalesce(p->>'description_ar', ''), coalesce(p->>'category', 'other'), (p->>'price_fils')::bigint,
            coalesce((p->>'stock')::int, 0), coalesce(array(select jsonb_array_elements_text(p->'images')), '{}'),
            coalesce((p->>'active')::boolean, true))
    returning * into r;
  else
    update store_products set title_ar = p->>'title_ar', description_ar = coalesce(p->>'description_ar', ''),
      category = coalesce(p->>'category', category), price_fils = (p->>'price_fils')::bigint, stock = (p->>'stock')::int,
      images = coalesce(array(select jsonb_array_elements_text(p->'images')), images), active = coalesce((p->>'active')::boolean, active)
    where id = pid returning * into r;
    if not found then raise exception 'product not found'; end if;
  end if;
  return r;
end $$;

-- ---------------------------------------------------------------- admin: orders

-- Delivery of won products. Shipping notifies the owner.
create or replace function admin_set_order_status(p_order uuid, p_status text) returns orders
language plpgsql security definer set search_path = public as $$
declare o orders;
begin
  if not is_admin() then raise exception 'admins only'; end if;
  if p_status not in ('pending','shipped','delivered','failed') then raise exception 'bad status'; end if;
  select * into o from orders where id = p_order for update;
  if not found then raise exception 'order not found'; end if;
  if o.address_id is null then raise exception 'no address yet'; end if;
  update orders set status = p_status,
    shipped_at = case when p_status = 'shipped' then now() else shipped_at end,
    delivered_at = case when p_status = 'delivered' then now() else delivered_at end
  where id = p_order returning * into o;
  if p_status = 'shipped' then
    insert into notifications (user_id, type, payload)
    select o.user_id, 'order_shipped', jsonb_build_object('product_id', a.product_id) from assignments a where a.id = o.assignment_id;
  end if;
  return o;
end $$;

-- Store orders. Cancelling returns the money and the stock.
create or replace function admin_set_store_order_status(p_order uuid, p_status text) returns store_orders
language plpgsql security definer set search_path = public as $$
declare o store_orders; it record;
begin
  if not is_admin() then raise exception 'admins only'; end if;
  if p_status not in ('pending','shipped','delivered','cancelled') then raise exception 'bad status'; end if;
  select * into o from store_orders where id = p_order for update;
  if not found then raise exception 'order not found'; end if;
  if o.status = 'cancelled' then raise exception 'order already cancelled'; end if;
  if p_status = 'cancelled' then
    if o.status = 'delivered' then raise exception 'delivered orders cannot be cancelled'; end if;
    for it in select * from store_order_items where order_id = o.id loop
      update store_products set stock = stock + it.qty where id = it.product_id;
    end loop;
    if o.paid_shopping_fils > 0 then perform _ledger(o.user_id, 'shopping', 'refund', o.paid_shopping_fils, 'store_order', o.id, 'store-refund-shop:' || o.id); end if;
    if o.paid_cash_fils > 0 then perform _ledger(o.user_id, 'cash', 'refund', o.paid_cash_fils, 'store_order', o.id, 'store-refund-cash:' || o.id); end if;
  end if;
  update store_orders set status = p_status where id = p_order returning * into o;
  return o;
end $$;

revoke all on function admin_save_product, admin_open_product, admin_cancel_product, admin_save_store_product,
  admin_set_order_status, admin_set_store_order_status from public, anon;
grant execute on function admin_save_product, admin_open_product, admin_cancel_product, admin_save_store_product,
  admin_set_order_status, admin_set_store_order_status to authenticated;

-- ---------------------------------------------------------------- product photos (Supabase Storage)

-- Public bucket "product-images": anyone can view, only admins can upload or delete.
do $$ begin
  if exists (select 1 from pg_namespace where nspname = 'storage') then
    insert into storage.buckets (id, name, public) values ('product-images', 'product-images', true) on conflict (id) do nothing;
    create policy product_images_read on storage.objects for select using (bucket_id = 'product-images');
    create policy product_images_admin_write on storage.objects for insert to authenticated
      with check (bucket_id = 'product-images' and public.is_admin());
    create policy product_images_admin_delete on storage.objects for delete to authenticated
      using (bucket_id = 'product-images' and public.is_admin());
  end if;
end $$;
