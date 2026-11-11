USE flower_shop;

-- =====================================================
-- 1. ТАБЛИЦЯ КОРИСТУВАЧІВ
-- =====================================================

CREATE TABLE IF NOT EXISTS Users (
    user_id INT AUTO_INCREMENT PRIMARY KEY,

    first_name VARCHAR(100) NOT NULL,
    last_name VARCHAR(100) NOT NULL,

    email VARCHAR(255) NOT NULL UNIQUE,

    password_hash VARCHAR(255) NOT NULL,

    created_at DATETIME NOT NULL
        DEFAULT CURRENT_TIMESTAMP
);


-- =====================================================
-- 2. ЗВ'ЯЗОК КОРИСТУВАЧІВ І ЗАМОВЛЕНЬ
-- =====================================================

-- Дозволяємо старим замовленням залишитися без акаунта.
-- Нові замовлення зареєстрованих покупців
-- будуть пов'язані з user_id.

ALTER TABLE Orders
ADD COLUMN user_id INT NULL;

ALTER TABLE Orders
ADD CONSTRAINT fk_orders_user
FOREIGN KEY (user_id)
REFERENCES Users(user_id)
ON UPDATE CASCADE
ON DELETE RESTRICT;

CREATE INDEX idx_orders_user
ON Orders(user_id);


-- =====================================================
-- 3. АСОРТИМЕНТ КВІТКОВОГО МАГАЗИНУ
-- =====================================================

INSERT INTO Products
(name, description, price, quantity, is_available)
VALUES

(
    'Букет червоних троянд',
    'Елегантний букет із 15 червоних троянд. Ідеальний подарунок для особливої людини.',
    1800.00,
    20,
    TRUE
),

(
    'Букет білих троянд',
    'Ніжна композиція з 11 білих троянд у стильному пакуванні.',
    1450.00,
    15,
    TRUE
),

(
    'Букет рожевих півоній',
    'Романтичний букет із 9 рожевих півоній.',
    2200.00,
    12,
    TRUE
),

(
    'Букет тюльпанів',
    'Весняний букет із 21 різнокольорового тюльпана.',
    1250.00,
    25,
    TRUE
),

(
    'Букет соняшників',
    'Яскрава композиція із соняшників та декоративної зелені.',
    950.00,
    18,
    TRUE
),

(
    'Букет білих лілій',
    'Вишуканий букет із білих лілій для урочистих подій.',
    1650.00,
    10,
    TRUE
),

(
    'Букет гортензій',
    'Повітряна композиція з блакитних та білих гортензій.',
    1950.00,
    14,
    TRUE
),

(
    'Букет ромашок',
    'Ніжний букет із білих ромашок у натуральному оформленні.',
    750.00,
    30,
    TRUE
);


-- =====================================================
-- 4. ПЕРЕВІРКА
-- =====================================================

SHOW TABLES;

SELECT * FROM Users;

SELECT
    product_id,
    name,
    price,
    quantity,
    is_available
FROM Products
ORDER BY product_id;

SELECT
    order_id,
    customer_id,
    user_id,
    status,
    total_amount
FROM Orders;