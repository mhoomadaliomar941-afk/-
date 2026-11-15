-- Demo data for a development project. Fake stores and products, no real people.
-- `supabase db reset` runs it after the migrations.
insert into partner_stores (name_ar, name_en, governorate, city, category) values
  ('سوبرماركت الحيّ', 'Neighbourhood Market', 'عمّان', 'عمّان', 'بقالة'),
  ('مكتبة القلم', 'Al-Qalam Books', 'عمّان', 'عمّان', 'قرطاسية'),
  ('إلكترونيات الشمال', 'North Electronics', 'إربد', 'إربد', 'إلكترونيات'),
  ('مخبز الزرقاء', 'Zarqa Bakery', 'الزرقاء', 'الزرقاء', 'مخابز'),
  ('أزياء العقبة', 'Aqaba Fashion', 'العقبة', 'العقبة', 'ملابس');

with p as (
  insert into products (title_ar, title_en, category, description_ar, specs, retail_price_fils, spot_price_fils, total_spots, max_spots_per_user) values
  ('هاتف ذكي برو ٢٥٦ جيجا', 'Pro smartphone 256GB', 'phone', 'هاتف رائد بشاشة ٦٫٧ إنش. جديد بالكرتونة مع كفالة الوكيل.', '{"الذاكرة":"256GB","الشاشة":"6.7\""}', 899000, 1000, 50, 5),
  ('ساعة ذكية الإصدار العاشر', 'Smart watch gen 10', 'watch', 'ساعة ذكية مقاومة للماء مع تتبّع النوم.', '{"الحجم":"46mm"}', 280000, 1000, 400, 10),
  ('جهاز ألعاب الجيل الخامس مع يدّين', 'Game console with two controllers', 'console', 'جهاز ألعاب منزلي بدقة 4K.', '{"التخزين":"1TB"}', 420000, 2000, 300, 10),
  ('سماعات لاسلكية بعزل الضوضاء', 'Noise-cancelling earbuds', 'audio', 'سماعات داخل الأذن مع عزل نشط للضوضاء.', '{"البطارية":"30 ساعة"}', 150000, 1000, 200, 5)
  returning id
)
select open_product(id) from p;
