INSERT INTO tenants (name, phone_number_id, business_context)
VALUES (
    'Example Portugal Business',
    '1098765432109876',
    'Replace this with verified business facts, services, prices, availability, and policies.'
)
ON CONFLICT (phone_number_id) DO NOTHING;
