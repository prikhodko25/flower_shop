-- =====================================================
-- 1. СТВОРЕННЯ БАЗИ ДАНИХ
-- =====================================================

CREATE DATABASE IF NOT EXISTS flower_shop
    CHARACTER SET utf8mb4
    COLLATE utf8mb4_unicode_ci;

USE flower_shop;


-- =====================================================
-- 2. ТАБЛИЦЯ КОРИСТУВАЧІВ
-- =====================================================

CREATE TABLE IF NOT EXISTS Users (
    user_id INT AUTO_INCREMENT PRIMARY KEY,
    first_name VARCHAR(100) NOT NULL,
    last_name VARCHAR(100) NOT NULL,
    email VARCHAR(255) NOT NULL,
    password_hash VARCHAR(255) NOT NULL,
    created_at DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP,

    UNIQUE KEY uq_users_email (email)
);


-- =====================================================
-- 3. ТАБЛИЦЯ КЛІЄНТІВ
-- =====================================================

CREATE TABLE IF NOT EXISTS Customers (
    customer_id INT AUTO_INCREMENT PRIMARY KEY,
    name VARCHAR(100) NOT NULL,
    phone VARCHAR(20) NOT NULL,
    email VARCHAR(100),

    -- Однаковий телефон дозволений у різних клієнтів.
    UNIQUE KEY uq_customers_email (email)
);


-- =====================================================
-- 4. ТАБЛИЦЯ ТОВАРІВ
-- =====================================================

CREATE TABLE IF NOT EXISTS Products (
    product_id INT AUTO_INCREMENT PRIMARY KEY,
    name VARCHAR(100) NOT NULL,
    description TEXT,
    price DECIMAL(10,2) NOT NULL,
    quantity INT NOT NULL DEFAULT 0,
    is_available BOOLEAN NOT NULL DEFAULT TRUE,

    CONSTRAINT chk_product_price
        CHECK (price >= 0),

    CONSTRAINT chk_product_quantity
        CHECK (quantity >= 0),

    INDEX idx_products_name (name)
);


-- =====================================================
-- 5. АСОРТИМЕНТ МАГАЗИНУ «КВІТКО»
-- =====================================================

-- ID товарів 1–8 відповідають фотографіям
-- у папці wwwroot/images.

-- Ціни, описи та кількість нижче є прикладом.
-- Для чинної бази збережіть фактичні значення.

INSERT INTO Products
    (product_id, name, description, price, quantity, is_available)
VALUES
    (
        1,
        'Букет червоних троянд',
        'Романтичний букет із червоних троянд',
        1200.00,
        20,
        TRUE
    ),
    (
        2,
        'Букет білих троянд',
        'Ніжний букет із білих троянд',
        1100.00,
        20,
        TRUE
    ),
    (
        3,
        'Букет рожевих півоній',
        'Пишний букет із рожевих півоній',
        1500.00,
        15,
        TRUE
    ),
    (
        4,
        'Букет тюльпанів',
        'Весняний букет із яскравих тюльпанів',
        850.00,
        25,
        TRUE
    ),
    (
        5,
        'Букет соняшників',
        'Сонячний букет із соняшників',
        900.00,
        15,
        TRUE
    ),
    (
        6,
        'Букет білих лілій',
        'Елегантний букет із білих лілій',
        1300.00,
        15,
        TRUE
    ),
    (
        7,
        'Букет гортензій',
        'Повітряний букет із ніжних гортензій',
        1400.00,
        15,
        TRUE
    ),
    (
        8,
        'Букет ромашок',
        'Легкий та ніжний букет із ромашок',
        650.00,
        25,
        TRUE
    );


-- =====================================================
-- 6. ТАБЛИЦЯ ЗАМОВЛЕНЬ
-- =====================================================

CREATE TABLE IF NOT EXISTS Orders (
    order_id INT AUTO_INCREMENT PRIMARY KEY,

    customer_id INT NOT NULL,

    user_id INT NULL,

    -- Дата і час оформлення замовлення
    order_date DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP,

    status ENUM(
        'Нове',
        'Прийнято',
        'В обробці',
        'Готове',
        'Доставляється',
        'Виконано',
        'Скасовано'
    ) NOT NULL DEFAULT 'Нове',

    delivery_method ENUM(
        'Доставка',
        'Самовивіз'
    ) NOT NULL DEFAULT 'Самовивіз',

    delivery_address VARCHAR(255) NULL,

    -- Лише дата отримання, без часу
    delivery_datetime DATE NULL,

    payment_method ENUM(
        'Готівка',
        'Картка',
        'Онлайн'
    ) NOT NULL DEFAULT 'Готівка',

    total_amount DECIMAL(12,2) NOT NULL DEFAULT 0.00,

    CONSTRAINT fk_orders_customer
        FOREIGN KEY (customer_id)
        REFERENCES Customers(customer_id)
        ON UPDATE CASCADE
        ON DELETE RESTRICT,

    CONSTRAINT fk_orders_user
        FOREIGN KEY (user_id)
        REFERENCES Users(user_id)
        ON UPDATE CASCADE
        ON DELETE RESTRICT,

    CONSTRAINT chk_order_total
        CHECK (total_amount >= 0),

    CONSTRAINT chk_delivery_address
        CHECK (
            delivery_method <> 'Доставка'
            OR delivery_address IS NOT NULL
        ),

    INDEX idx_orders_date (order_date),
    INDEX idx_orders_status (status),
    INDEX idx_orders_customer (customer_id),
    INDEX idx_orders_user (user_id)
);


-- =====================================================
-- 7. ТАБЛИЦЯ ПОЗИЦІЙ ЗАМОВЛЕННЯ
-- =====================================================

CREATE TABLE IF NOT EXISTS OrderItems (
    order_item_id INT AUTO_INCREMENT PRIMARY KEY,

    order_id INT NOT NULL,
    product_id INT NOT NULL,

    quantity INT NOT NULL,
    unit_price DECIMAL(10,2) NOT NULL,

    line_total DECIMAL(12,2)
        GENERATED ALWAYS AS (quantity * unit_price) STORED,

    CONSTRAINT fk_orderitems_order
        FOREIGN KEY (order_id)
        REFERENCES Orders(order_id)
        ON UPDATE CASCADE
        ON DELETE CASCADE,

    CONSTRAINT fk_orderitems_product
        FOREIGN KEY (product_id)
        REFERENCES Products(product_id)
        ON UPDATE CASCADE
        ON DELETE RESTRICT,

    CONSTRAINT chk_orderitem_quantity
        CHECK (quantity > 0),

    CONSTRAINT chk_orderitem_price
        CHECK (unit_price >= 0),

    UNIQUE KEY uq_order_product (order_id, product_id),

    INDEX idx_orderitems_product (product_id)
);


-- =====================================================
-- 8. ТРИГЕР: ПЕРЕВІРКА КІЛЬКОСТІ ТОВАРУ
-- =====================================================

DELIMITER $$

CREATE TRIGGER trg_orderitems_before_insert
BEFORE INSERT ON OrderItems
FOR EACH ROW
BEGIN
    DECLARE stock_quantity INT;
    DECLARE current_price DECIMAL(10,2);

    SELECT quantity, price
    INTO stock_quantity, current_price
    FROM Products
    WHERE product_id = NEW.product_id
    FOR UPDATE;

    IF NEW.quantity > stock_quantity THEN
        SIGNAL SQLSTATE '45000'
            SET MESSAGE_TEXT =
                'Недостатньо товару на складі';
    END IF;

    -- Фіксуємо актуальну ціну товару
    SET NEW.unit_price = current_price;
END$$


-- =====================================================
-- 9. ТРИГЕР: ОНОВЛЕННЯ ЗАЛИШКУ ПІСЛЯ ДОДАВАННЯ
-- =====================================================

CREATE TRIGGER trg_orderitems_after_insert
AFTER INSERT ON OrderItems
FOR EACH ROW
BEGIN
    UPDATE Products
    SET quantity = quantity - NEW.quantity
    WHERE product_id = NEW.product_id;

    UPDATE Orders
    SET total_amount = (
        SELECT COALESCE(SUM(line_total), 0)
        FROM OrderItems
        WHERE order_id = NEW.order_id
    )
    WHERE order_id = NEW.order_id;
END$$


-- =====================================================
-- 10. ТРИГЕР: ПЕРЕВІРКА ЗМІНИ КІЛЬКОСТІ
-- =====================================================

CREATE TRIGGER trg_orderitems_before_update
BEFORE UPDATE ON OrderItems
FOR EACH ROW
BEGIN
    DECLARE stock_quantity INT;

    IF NEW.order_id <> OLD.order_id
       OR NEW.product_id <> OLD.product_id THEN

        SIGNAL SQLSTATE '45000'
            SET MESSAGE_TEXT =
                'Не можна змінювати замовлення або товар позиції';
    END IF;

    SELECT quantity
    INTO stock_quantity
    FROM Products
    WHERE product_id = OLD.product_id
    FOR UPDATE;

    IF NEW.quantity > OLD.quantity + stock_quantity THEN
        SIGNAL SQLSTATE '45000'
            SET MESSAGE_TEXT =
                'Недостатньо товару для зміни кількості';
    END IF;

    -- Зберігаємо початкову ціну товару
    SET NEW.unit_price = OLD.unit_price;
END$$


-- =====================================================
-- 11. ТРИГЕР: ОНОВЛЕННЯ ЗАЛИШКУ ПІСЛЯ ЗМІНИ
-- =====================================================

CREATE TRIGGER trg_orderitems_after_update
AFTER UPDATE ON OrderItems
FOR EACH ROW
BEGIN
    UPDATE Products
    SET quantity = quantity + OLD.quantity - NEW.quantity
    WHERE product_id = NEW.product_id;

    UPDATE Orders
    SET total_amount = (
        SELECT COALESCE(SUM(line_total), 0)
        FROM OrderItems
        WHERE order_id = NEW.order_id
    )
    WHERE order_id = NEW.order_id;
END$$


-- =====================================================
-- 12. ТРИГЕР: ПОВЕРНЕННЯ ТОВАРУ ПІСЛЯ ВИДАЛЕННЯ
-- =====================================================

CREATE TRIGGER trg_orderitems_after_delete
AFTER DELETE ON OrderItems
FOR EACH ROW
BEGIN
    UPDATE Products
    SET quantity = quantity + OLD.quantity
    WHERE product_id = OLD.product_id;

    UPDATE Orders
    SET total_amount = (
        SELECT COALESCE(SUM(line_total), 0)
        FROM OrderItems
        WHERE order_id = OLD.order_id
    )
    WHERE order_id = OLD.order_id;
END$$


-- =====================================================
-- 13. ТРИГЕР: ПОВЕРНЕННЯ ТОВАРУ ПРИ СКАСУВАННІ
-- =====================================================

CREATE TRIGGER trg_orders_before_update
BEFORE UPDATE ON Orders
FOR EACH ROW
BEGIN
    IF OLD.status <> 'Скасовано'
       AND NEW.status = 'Скасовано' THEN

        UPDATE Products p
        JOIN OrderItems oi
            ON oi.product_id = p.product_id
        SET p.quantity = p.quantity + oi.quantity
        WHERE oi.order_id = OLD.order_id;

    END IF;

    IF OLD.status = 'Скасовано'
       AND NEW.status <> 'Скасовано' THEN

        IF EXISTS (
            SELECT 1
            FROM OrderItems oi
            JOIN Products p
                ON p.product_id = oi.product_id
            WHERE oi.order_id = OLD.order_id
              AND p.quantity < oi.quantity
        ) THEN
            SIGNAL SQLSTATE '45000'
                SET MESSAGE_TEXT =
                    'Недостатньо товару для повторного відкриття замовлення';
        END IF;

        UPDATE Products p
        JOIN OrderItems oi
            ON oi.product_id = p.product_id
        SET p.quantity = p.quantity - oi.quantity
        WHERE oi.order_id = OLD.order_id;

    END IF;
END$$

DELIMITER ;


-- =====================================================
-- 14. ПЕРЕГЛЯД ТАБЛИЦЬ
-- =====================================================

SHOW TABLES;

SELECT * FROM Users;
SELECT * FROM Customers;
SELECT * FROM Products;
SELECT * FROM Orders;
SELECT * FROM OrderItems;