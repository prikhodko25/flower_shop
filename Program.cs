using MySqlConnector;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;

var builder = WebApplication.CreateBuilder(args);

// =====================================================
// 1. НАСТРОЙКА АВТОРИЗАЦИИ
// =====================================================

builder.Services
    .AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
    .AddCookie(options =>
    {
        options.Cookie.Name = "Kvitko.Auth";
        options.Cookie.HttpOnly = true;
        options.Cookie.SameSite = SameSiteMode.Strict;
        options.Cookie.SecurePolicy = CookieSecurePolicy.SameAsRequest;

        options.ExpireTimeSpan = TimeSpan.FromDays(7);

        options.Events.OnRedirectToLogin = context =>
        {
            context.Response.StatusCode =
                StatusCodes.Status401Unauthorized;

            return Task.CompletedTask;
        };

        options.Events.OnRedirectToAccessDenied = context =>
        {
            context.Response.StatusCode =
                StatusCodes.Status403Forbidden;

            return Task.CompletedTask;
        };
    });

builder.Services.AddAuthorization();

var app = builder.Build();

app.UseDefaultFiles();
app.UseStaticFiles();

app.UseAuthentication();
app.UseAuthorization();

// Защита API от запросов с других сайтов
app.Use(async (context, next) =>
{
    if (context.Request.Path.StartsWithSegments("/api") &&
        !HttpMethods.IsGet(context.Request.Method) &&
        !HttpMethods.IsHead(context.Request.Method))
    {
        var origin = context.Request.Headers.Origin.ToString();

        if (!string.IsNullOrEmpty(origin) &&
            !string.Equals(
                origin,
                $"{context.Request.Scheme}://{context.Request.Host}",
                StringComparison.OrdinalIgnoreCase))
        {
            context.Response.StatusCode =
                StatusCodes.Status403Forbidden;

            return;
        }
    }

    await next();
});

// =====================================================
// 2. ПРОВЕРКА ПОДКЛЮЧЕНИЯ К MYSQL
// =====================================================

app.MapGet("/api/test-db", async (IConfiguration config) =>
{
    try
    {
        await using var connection =
            new MySqlConnection(GetConnectionString(config));

        await connection.OpenAsync();

        return Results.Ok("Подключение к MySQL успешно!");
    }
    catch (Exception ex) when (
        ex is MySqlException or InvalidOperationException)
    {
        return Results.Problem(
            "Не удалось подключиться к MySQL.");
    }
});

// =====================================================
// 3. ПОЛУЧЕНИЕ ТОВАРОВ ИЗ MYSQL
// =====================================================

app.MapGet("/api/products", async (IConfiguration config) =>
{
    try
    {
        var products = new List<object>();

        await using var connection =
            new MySqlConnection(GetConnectionString(config));

        await connection.OpenAsync();

        await using var command = new MySqlCommand(
            @"SELECT
                product_id,
                name,
                description,
                price,
                quantity,
                is_available
              FROM Products
              ORDER BY product_id",
            connection);

        await using var reader =
            await command.ExecuteReaderAsync();

        while (await reader.ReadAsync())
        {
            products.Add(new
            {
                product_id = reader.GetInt32("product_id"),

                name = reader.GetString("name"),

                description = reader.IsDBNull(
                    reader.GetOrdinal("description"))
                    ? ""
                    : reader.GetString("description"),

                price = reader.GetDecimal("price"),

                quantity = reader.GetInt32("quantity"),

                is_available = reader.GetBoolean("is_available")
            });
        }

        return Results.Ok(products);
    }
    catch (Exception ex) when (
        ex is MySqlException or InvalidOperationException)
    {
        return Results.Problem(
            "Ошибка чтения товаров из MySQL.");
    }
});

// =====================================================
// 4. РЕГИСТРАЦИЯ ПОЛЬЗОВАТЕЛЯ
// =====================================================

app.MapPost("/api/register", async (
    RegisterRequest request,
    IConfiguration config,
    HttpContext http) =>
{
    var first = request.FirstName?.Trim() ?? "";
    var last = request.LastName?.Trim() ?? "";

    var email =
        request.Email?.Trim().ToLowerInvariant() ?? "";

    var password = request.Password ?? "";

    if (first.Length is < 1 or > 100 ||
        last.Length is < 1 or > 100 ||
        email.Length is < 3 or > 255 ||
        !Regex.IsMatch(
            email,
            @"^[^\s@]+@[^\s@]+\.[^\s@]+$") ||
        password.Length is < 8 or > 128)
    {
        return Results.BadRequest(
            "Укажите имя, фамилию, правильную почту " +
            "и пароль длиной 8–128 символов.");
    }

    try
    {
        await using var connection =
            new MySqlConnection(GetConnectionString(config));

        await connection.OpenAsync();

        await using var command = new MySqlCommand(
            @"INSERT INTO Users
                (
                    first_name,
                    last_name,
                    email,
                    password_hash
                )
              VALUES
                (
                    @first,
                    @last,
                    @email,
                    @hash
                )",
            connection);

        command.Parameters.AddWithValue("@first", first);
        command.Parameters.AddWithValue("@last", last);
        command.Parameters.AddWithValue("@email", email);

        command.Parameters.AddWithValue(
            "@hash",
            HashPassword(password));

        await command.ExecuteNonQueryAsync();

        var userId = command.LastInsertedId;

        await SignInUser(
            http,
            userId,
            first,
            last,
            email);

        return Results.Ok(new
        {
            user_id = userId,
            first_name = first,
            last_name = last,
            email
        });
    }
    catch (MySqlException ex) when (ex.Number == 1062)
    {
        return Results.Conflict(
            "Эта электронная почта уже зарегистрирована.");
    }
    catch (Exception ex) when (
        ex is MySqlException or InvalidOperationException)
    {
        return Results.Problem(
            "Ошибка регистрации. Проверьте таблицу Users.");
    }
});

// =====================================================
// 5. ВХОД В АККАУНТ
// =====================================================

app.MapPost("/api/login", async (
    LoginRequest request,
    IConfiguration config,
    HttpContext http) =>
{
    var email =
        request.Email?.Trim().ToLowerInvariant() ?? "";

    var password = request.Password ?? "";

    if (email.Length == 0 || password.Length == 0)
    {
        return Results.BadRequest(
            "Введите почту и пароль.");
    }

    try
    {
        await using var connection =
            new MySqlConnection(GetConnectionString(config));

        await connection.OpenAsync();

        await using var command = new MySqlCommand(
            @"SELECT
                user_id,
                first_name,
                last_name,
                email,
                password_hash
              FROM Users
              WHERE email = @email
              LIMIT 1",
            connection);

        command.Parameters.AddWithValue("@email", email);

        await using var reader =
            await command.ExecuteReaderAsync();

        if (!await reader.ReadAsync())
        {
            return Results.Json(
                new
                {
                    message = "Неверная почта или пароль."
                },
                statusCode: 401);
        }

        var userId = reader.GetInt64("user_id");

        var first = reader.GetString("first_name");
        var last = reader.GetString("last_name");

        var savedEmail = reader.GetString("email");

        var hash = reader.GetString("password_hash");

        if (!VerifyPassword(password, hash))
        {
            return Results.Json(
                new
                {
                    message = "Неверная почта или пароль."
                },
                statusCode: 401);
        }

        await reader.CloseAsync();

        await SignInUser(
            http,
            userId,
            first,
            last,
            savedEmail);

        return Results.Ok(new
        {
            user_id = userId,
            first_name = first,
            last_name = last,
            email = savedEmail
        });
    }
    catch (Exception ex) when (
        ex is MySqlException or InvalidOperationException)
    {
        return Results.Problem(
            "Ошибка входа в аккаунт.");
    }
});

// =====================================================
// 6. ВЫХОД ИЗ АККАУНТА
// =====================================================

app.MapPost("/api/logout", async (HttpContext http) =>
{
    await http.SignOutAsync(
        CookieAuthenticationDefaults.AuthenticationScheme);

    return Results.Ok(new
    {
        message = "Вы вышли из аккаунта."
    });

}).RequireAuthorization();

// =====================================================
// 7. ИНФОРМАЦИЯ О ТЕКУЩЕМ ПОЛЬЗОВАТЕЛЕ
// =====================================================

app.MapGet("/api/me", (ClaimsPrincipal user) =>
{
    if (user.Identity?.IsAuthenticated != true)
    {
        return Results.Unauthorized();
    }

    return Results.Ok(new
    {
        user_id = long.Parse(
            user.FindFirstValue(
                ClaimTypes.NameIdentifier)!),

        first_name = user.FindFirstValue(
            ClaimTypes.GivenName),

        last_name = user.FindFirstValue(
            ClaimTypes.Surname),

        email = user.FindFirstValue(
            ClaimTypes.Email)
    });

}).RequireAuthorization();

// =====================================================
// 8. ПОЛУЧЕНИЕ ЗАКАЗОВ ПОЛЬЗОВАТЕЛЯ
// =====================================================

app.MapGet("/api/orders", async (
    IConfiguration config,
    ClaimsPrincipal user) =>
{
    var userId = long.Parse(
        user.FindFirstValue(
            ClaimTypes.NameIdentifier)!);

    try
    {
        var orders = new List<object>();

        await using var connection =
            new MySqlConnection(GetConnectionString(config));

        await connection.OpenAsync();

        await using var command = new MySqlCommand(
            @"SELECT
                order_id,
                order_date,
                status,
                total_amount,
                delivery_method,
                payment_method
              FROM Orders
              WHERE user_id = @userId
              ORDER BY order_id DESC",
            connection);

        command.Parameters.AddWithValue(
            "@userId",
            userId);

        await using var reader =
            await command.ExecuteReaderAsync();

        while (await reader.ReadAsync())
        {
            orders.Add(new
            {
                order_id = reader.GetInt32("order_id"),

                order_date = reader.IsDBNull(
                    reader.GetOrdinal("order_date"))
                    ? (DateTime?)null
                    : reader.GetDateTime("order_date"),

                status = reader.GetString("status"),

                total_amount =
                    reader.GetDecimal("total_amount"),

                delivery_method =
                    reader.GetString("delivery_method"),

                payment_method =
                    reader.GetString("payment_method")
            });
        }

        return Results.Ok(orders);
    }
    catch (Exception ex) when (
        ex is MySqlException or InvalidOperationException)
    {
        return Results.Problem(
            "Ошибка получения заказов из MySQL.");
    }

}).RequireAuthorization();

// =====================================================
// 9. СОЗДАНИЕ НОВОГО ЗАКАЗА
// =====================================================

app.MapPost("/api/orders", async (
    OrderRequest order,
    IConfiguration config,
    ClaimsPrincipal user) =>
{
    // Проверка имени и телефона
    if (string.IsNullOrWhiteSpace(order.CustomerName) ||
        string.IsNullOrWhiteSpace(order.Phone))
    {
        return Results.BadRequest(
            "Укажите имя и телефон покупателя.");
    }

    if (order.CustomerName.Length > 100 ||
        order.Phone.Length > 20 ||
        (order.Email?.Length ?? 0) > 100 ||
        (order.DeliveryAddress?.Length ?? 0) > 255)
    {
        return Results.BadRequest(
            "Одно из полей превышает допустимую длину.");
    }

    // Проверка корзины
    if (order.Items == null || order.Items.Count == 0)
    {
        return Results.BadRequest("Корзина пустая.");
    }

    if (order.Items.Any(x =>
            x.ProductId <= 0 || x.Quantity <= 0) ||
        order.Items.GroupBy(x => x.ProductId)
            .Any(g => g.Count() > 1))
    {
        return Results.BadRequest(
            "Некорректные данные товаров.");
    }

    // Проверка доставки
    if (order.DeliveryMethod != "Доставка" &&
        order.DeliveryMethod != "Самовивіз")
    {
        return Results.BadRequest(
            "Некорректный способ получения заказа.");
    }

    if (order.DeliveryMethod == "Доставка" &&
        string.IsNullOrWhiteSpace(order.DeliveryAddress))
    {
        return Results.BadRequest(
            "Укажите адрес доставки.");
    }

    // Проверка оплаты
    if (order.PaymentMethod != "Готівка" &&
        order.PaymentMethod != "Картка" &&
        order.PaymentMethod != "Онлайн")
    {
        return Results.BadRequest(
            "Некорректный способ оплаты.");
    }

    var userId = long.Parse(
        user.FindFirstValue(
            ClaimTypes.NameIdentifier)!);

    var accountEmail =
        user.FindFirstValue(ClaimTypes.Email)!;

    try
    {
        await using var connection =
            new MySqlConnection(GetConnectionString(config));

        await connection.OpenAsync();

        await using var transaction =
            await connection.BeginTransactionAsync();

        try
        {
            decimal total = 0;

            var items =
                new List<(int Id, int Qty, decimal Price)>();

            // Проверяем товары и остатки
            foreach (var item in order.Items
                .OrderBy(x => x.ProductId))
            {
                await using var cmd = new MySqlCommand(
                    @"SELECT
                        price,
                        quantity,
                        is_available
                      FROM Products
                      WHERE product_id = @id
                      FOR UPDATE",
                    connection,
                    transaction);

                cmd.Parameters.AddWithValue(
                    "@id",
                    item.ProductId);

                await using var reader =
                    await cmd.ExecuteReaderAsync();

                if (!await reader.ReadAsync())
                {
                    await reader.CloseAsync();

                    await transaction.RollbackAsync();

                    return Results.BadRequest(
                        $"Товар №{item.ProductId} не найден.");
                }

                var price = reader.GetDecimal("price");

                var stock = reader.GetInt32("quantity");

                var available =
                    reader.GetBoolean("is_available");

                await reader.CloseAsync();

                if (!available || stock < item.Quantity)
                {
                    await transaction.RollbackAsync();

                    return Results.BadRequest(
                        $"Недостаточно товара №{item.ProductId}.");
                }

                total += price * item.Quantity;

                items.Add((
                    item.ProductId,
                    item.Quantity,
                    price
                ));
            }

            // Сохраняем контактные данные покупателя
            await using var customerCmd = new MySqlCommand(
                @"INSERT INTO Customers
                    (
                        name,
                        phone,
                        email
                    )
                  VALUES
                    (
                        @name,
                        @phone,
                        @email
                    )
                  ON DUPLICATE KEY UPDATE
                    customer_id = LAST_INSERT_ID(customer_id)",
                connection,
                transaction);

            customerCmd.Parameters.AddWithValue(
                "@name",
                order.CustomerName.Trim());

            customerCmd.Parameters.AddWithValue(
                "@phone",
                order.Phone.Trim());

            customerCmd.Parameters.AddWithValue(
                "@email",
                accountEmail);

            await customerCmd.ExecuteNonQueryAsync();

            var customerId = customerCmd.LastInsertedId;

            // Сохраняем заказ
            await using var orderCmd = new MySqlCommand(
                @"INSERT INTO Orders
                    (
                        customer_id,
                        user_id,
                        status,
                        delivery_method,
                        delivery_address,
                        delivery_datetime,
                        payment_method,
                        total_amount
                    )
                  VALUES
                    (
                        @customerId,
                        @userId,
                        'Нове',
                        @deliveryMethod,
                        @deliveryAddress,
                        @deliveryDateTime,
                        @paymentMethod,
                        @total
                    )",
                connection,
                transaction);

            orderCmd.Parameters.AddWithValue(
                "@customerId",
                customerId);

            orderCmd.Parameters.AddWithValue(
                "@userId",
                userId);

            orderCmd.Parameters.AddWithValue(
                "@deliveryMethod",
                order.DeliveryMethod);

            orderCmd.Parameters.AddWithValue(
                "@deliveryAddress",
                order.DeliveryMethod == "Доставка"
                    ? (object)order.DeliveryAddress!.Trim()
                    : DBNull.Value);

            orderCmd.Parameters.AddWithValue(
                "@deliveryDateTime",
                order.DeliveryDateTime.HasValue
                    ? (object)order.DeliveryDateTime.Value.Date
                    : DBNull.Value);

            orderCmd.Parameters.AddWithValue(
                "@paymentMethod",
                order.PaymentMethod);

            orderCmd.Parameters.AddWithValue(
                "@total",
                total);

            await orderCmd.ExecuteNonQueryAsync();

            var orderId = orderCmd.LastInsertedId;

            // Сохраняем товары заказа
            foreach (var item in items)
            {
                await using var itemCmd = new MySqlCommand(
                    @"INSERT INTO OrderItems
                        (
                            order_id,
                            product_id,
                            quantity,
                            unit_price
                        )
                      VALUES
                        (
                            @orderId,
                            @id,
                            @qty,
                            @price
                        )",
                    connection,
                    transaction);

                itemCmd.Parameters.AddWithValue(
                    "@orderId",
                    orderId);

                itemCmd.Parameters.AddWithValue(
                    "@id",
                    item.Id);

                itemCmd.Parameters.AddWithValue(
                    "@qty",
                    item.Qty);

                itemCmd.Parameters.AddWithValue(
                    "@price",
                    item.Price);

                await itemCmd.ExecuteNonQueryAsync();

                // Остатки обновляет триггер MySQL.
                // Дополнительный UPDATE Products здесь не нужен.
            }

            await transaction.CommitAsync();

            return Results.Ok(new
            {
                order_id = orderId,
                total_amount = total,
                status = "Нове",

                message =
                    "Замовлення успішно оформлено!"
            });
        }
        catch
        {
            await transaction.RollbackAsync();
            throw;
        }
    }
    catch (MySqlException)
    {
        return Results.Problem(
            "Ошибка сохранения заказа в MySQL. " +
            "Проверьте таблицы и триггеры.");
    }

}).RequireAuthorization();

// =====================================================
// 10. ЗАПУСК СЕРВЕРА
// =====================================================

app.Run();

// =====================================================
// 11. ПОДКЛЮЧЕНИЕ К MYSQL
// =====================================================

static string GetConnectionString(IConfiguration config)
{
    return config.GetConnectionString("MySqlConnection")
        ?? throw new InvalidOperationException(
            "Не настроено подключение к MySQL.");
}

// =====================================================
// 12. БЕЗОПАСНОЕ ХЕШИРОВАНИЕ ПАРОЛЕЙ
// =====================================================

static string HashPassword(string password)
{
    byte[] salt = RandomNumberGenerator.GetBytes(16);

    byte[] hash = Rfc2898DeriveBytes.Pbkdf2(
        password,
        salt,
        210_000,
        HashAlgorithmName.SHA256,
        32);

    return
        $"pbkdf2-sha256$210000$" +
        $"{Convert.ToBase64String(salt)}$" +
        $"{Convert.ToBase64String(hash)}";
}

// =====================================================
// 13. ПРОВЕРКА ПАРОЛЯ
// =====================================================

static bool VerifyPassword(
    string password,
    string stored)
{
    try
    {
        var parts = stored.Split('$');

        if (parts.Length != 4 ||
            parts[0] != "pbkdf2-sha256")
        {
            return false;
        }

        if (!int.TryParse(
                parts[1],
                out int iterations) ||
            iterations < 100_000 ||
            iterations > 1_000_000)
        {
            return false;
        }

        var salt =
            Convert.FromBase64String(parts[2]);

        var expected =
            Convert.FromBase64String(parts[3]);

        if (salt.Length != 16 ||
            expected.Length != 32)
        {
            return false;
        }

        var actual = Rfc2898DeriveBytes.Pbkdf2(
            password,
            salt,
            iterations,
            HashAlgorithmName.SHA256,
            expected.Length);

        return CryptographicOperations.FixedTimeEquals(
            actual,
            expected);
    }
    catch (FormatException)
    {
        return false;
    }
}

// =====================================================
// 14. СОЗДАНИЕ СЕССИИ ПОЛЬЗОВАТЕЛЯ
// =====================================================

static async Task SignInUser(
    HttpContext http,
    long id,
    string first,
    string last,
    string email)
{
    var claims = new[]
    {
        new Claim(
            ClaimTypes.NameIdentifier,
            id.ToString()),

        new Claim(
            ClaimTypes.GivenName,
            first),

        new Claim(
            ClaimTypes.Surname,
            last),

        new Claim(
            ClaimTypes.Email,
            email)
    };

    var identity = new ClaimsIdentity(
        claims,
        CookieAuthenticationDefaults.AuthenticationScheme);

    await http.SignInAsync(
        CookieAuthenticationDefaults.AuthenticationScheme,
        new ClaimsPrincipal(identity));
}

// =====================================================
// 15. МОДЕЛИ ДАННЫХ
// =====================================================

// Данные регистрации
public record RegisterRequest(
    string FirstName,
    string LastName,
    string Email,
    string Password
);

// Данные входа
public record LoginRequest(
    string Email,
    string Password
);

// Товар в заказе
public record OrderItemRequest(
    int ProductId,
    int Quantity
);

// Данные заказа
public record OrderRequest(
    string CustomerName,
    string Phone,
    string? Email,
    string DeliveryMethod,
    string? DeliveryAddress,
    DateTime? DeliveryDateTime,
    string PaymentMethod,
    List<OrderItemRequest> Items
);