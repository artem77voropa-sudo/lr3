# Лабораторна робота №3
## Автентифікація та безпечна робота з обліковими даними

Студент: **Воропаєв Артем Максимович**, **КБ-41**. Baseline **2-A**. Рівень **«Добре»**.
Дата перевірки: 21.10.2026. Windows, VS Code, .NET SDK 10.0.402, PostgreSQL у Docker.

## Початковий стан і відтворення

Робота продовжує власну ЛР2: `v0.2.0`, commit `00bf3ecb05624fd6390a6b829c07be3fe2c30599`.
Гілка: `lab/3-authentication`. Scaffold: `lab-03-start-v1`; [квитанція](../.scaffolds/lab-03.json).
Installer спочатку перевірено dry run, потім виконано `--write`. Версії пакетів узято зі scaffold;
`dotnet restore SecureLab.sln --locked-mode` пройшов. Код ЛР2 не замінено starter-проєктом.
Початковий scaffold з власними регресіями ЛР2: 43 тести пройдено.

Локальний порт БД у цьому середовищі — **55432**: Windows зарезервувала діапазон із 54329.
Зміна передається через `POSTGRES_PORT` та `ConnectionStrings__SecureLab`, без зміни tracked конфігурації.
API залишається `http://localhost:5080`. Навчальний пароль надходить через `SeedUsers__Password`;
значення не включено в Git, звіт або HTTP-файли. На іншому ПК пароль задається власний за policy.

Команди в корені проєкту після налаштування локальних змінних середовища:

```powershell
dotnet restore SecureLab.sln --locked-mode
dotnet build SecureLab.sln -c Release
docker compose --env-file infra/.env.example -f infra/compose.yaml up -d --wait
dotnet run --project src/SecureLab.Api -c Release -- --reset-database
dotnet test tests/SecureLab.Api.Tests -c Release
dotnet run --project src/SecureLab.Api -c Release
```

Reset запускається лише для власної синтетичної БД, зі зупиненим API. Він видаляє навчальні записи.
Перевірки двох послідовних reset і тести після кожного наведено в [доказах](evidence/lr03/verification.md).

Репозиторій для подання: [artem77voropa-sudo/lr3](https://github.com/artem77voropa-sudo/lr3).
Гілка подання — `lab/3-authentication`; підсумковий стан позначається тегом `v0.3.0`.
Після публікації commit цього стану можна визначити командою `git rev-list -n 1 v0.3.0`.
Коміт, тег і push студент виконує самостійно після перегляду змін.

## Надана інфраструктура та власні зміни

Scaffold надав `ApplicationUser : IdentityUser<Guid>`, EF stores, migration/designer/snapshot,
чотири акаунти й ролі, зв’язки доменних сутностей, reset, реєстрацію сервісів, cookie helper та mechanics test.
Ці файли не видаються за власну розробку. Єдиний user store — Identity, `StudyUser` не зареєстрований у EF.
Збережені Guid Alice, Bob, Morgan, Admin та три зв’язки: власник інциденту, автор коментаря, автор зміни статусу.

Власна частина:

- [DTO](../src/SecureLab.Api/Presentation/Contracts/AuthenticationContracts.cs) і [register/login/me/logout](../src/SecureLab.Api/Scaffolding/Lab03Identity.cs).
- [Захищений POST і власник із principal](../src/SecureLab.Api/Scaffolding/Lab02Endpoints.cs).
- [Браузерна сторінка](../src/SecureLab.Api/Client/auth-lab.html) та [її логіка](../src/SecureLab.Api/Client/auth-lab.js).
- Власні security, registration, cookie/rate limit, seed і logging tests у `tests/SecureLab.Api.Tests`.
- Тести POST ЛР2 тепер входять реальним cookie-клієнтом Alice, зберігаючи очікування 201/400/409.

## CP-01: реєстрація та пароль

Request містить лише `userName`, `email`, `displayName`, `password`. UserName й email обов’язкові,
початкова довжина до 256; displayName до 120, не порожній і без керувальних символів.
UserName допускає латинські літери, цифри й `-._@+`; email перевіряється як адреса.
Для цих трьох текстових полів виконується Trim. Пароль **не** обрізається й не нормалізується.
Його вимоги перевіряє стандартний Identity: від 10 символів, велика/мала латинські літери, цифра та спецсимвол.

Некоректні поля → **400 ValidationProblem**; зайняте ім’я або email → **409 Problem Details** з одним
узгодженим повідомленням без уточнення зайнятого поля. Це послідовний контракт, але 409 залишається
каналом перебору існування акаунтів; однаковий текст його повністю не усуває.
Зайві `id`/`role` ігноруються: Id генерує сервер, роль завжди Reporter.
Успіх → **201**, Location `/api/me`, тільки `id`, `userName`, `displayName`. Автоматичного входу немає.

Маршрут: JSON → DTO → перевірки → `UserManager.CreateAsync(user, password)` → Identity store → safe DTO.
Власного хешування немає. Identity зберігає PasswordHash, а не відкритий пароль; стандартний verifier
повторює обчислення за параметрами збереженого формату та перевіряє результат. Випадкова сіль дає різні
представлення однакового пароля в Alice й Bob. Тест перевіряє це лише логічними результатами без друку хешів.
[Схема identity_users](evidence/lr03/identity-schema.txt) показує назви/типи стовпців, а не дані.

## CP-02: login, principal, cookie, logout

Початковий `/api/me` довіряв `X-Demo-UserId`: анонімний запит із Guid Bob повертав **200** і Bob.
[Фактичний результат до виправлення](evidence/lr03/before-me.txt).
Після виправлення endpoint має `.RequireAuthorization()`, а id, userName та role беруться лише з claims
`HttpContext.User`. Немає читання клієнтського header або запасного сталого id.

Шлях довіри: cookie → handler `IdentityConstants.ApplicationScheme` → перевірений principal →
`HttpContext.User` → endpoint. `UseAuthentication()` стоїть перед `UseAuthorization()`.
Анонімний `/api/me` і POST інциденту → **401 application/problem+json**, без 302, Location або Bearer challenge.
Невідомий користувач і хибний пароль → однакові 401, Content-Type та стабільні поля Problem Details.
TraceId є діагностикою запиту, його значення між двома відмовами не прирівнюються.

Cookie: HttpOnly=true, SameSite=Lax. У Development Secure=SameAsRequest; у Production Secure=Always.
Тести перевіряють фактичні атрибути відповіді для Development HTTP/HTTPS та Production HTTPS.
Cookie сеансова; ticket має стандартні **14 днів** і sliding expiration. HTTPS test transport не доводить
наявність production TLS. У локальному HTTP атрибут Secure відсутній — це очікувана межа цього стенда.

Logout викликає `SignInManager.SignOutAsync()`, повертає **204**. Той самий cookie-клієнт після виходу
одержує 401. Це не гарантія відкликання іншої раніше скопійованої cookie або всіх паралельних сеансів.

## CP-03: власник і збереження ЛР2

POST `/api/incidents` захищений декларативно. Після валідації id читається з
`ClaimTypes.NameIdentifier` та призначається `Incident.OwnerUserId`. Request DTO не містить власника.
Bob створює інцидент із зайвим `ownerUserId=Alice`; у БД фактичний owner залишається Bob.
Окремо перевірено create без owner. Сталий `DbSeeder.AliceId` із створення вилучено.
Правило конфлікту title із ЛР2 залишається глобальним для незакритих статусів; тимчасова політика захисту
«лише той самий власник» сюди не переносилася. Пошук, escaping ILIKE, сортування, Take(50), валідація й UTC збережені.

## Добрий рівень: обмеження входів, події та браузер

Login: **10 запитів / 60 секунд / IP**, fixed window, QueueLimit=0, перевищення → **429**.
Ключ береться з `RemoteIpAddress`, а не довільного forwarded header або імені користувача.
Відсутній IP у TestServer → спільний ключ `unknown`. Два клієнти одного host ділять quota;
новий HttpClient її не скидає. Регресія використовує справжнє хвилинне вікно, перевіряє 10 відмов 401,
наступний 429 і успішний вхід після 62 секунд. Інші endpoints не обмежені цією політикою.
Спільний NAT ділить квоту; розподілений перебір IP і кілька серверів потребують іншого рішення.
Ключ за username не обрано, щоб нападник не міг так просто витратити квоту конкретного чужого акаунта.

Подія містить тільки `EventType`, `UtcTime`, `Result`, `TraceId`. Не записуються ім’я, email, IP,
password, hash, cookie, token, Authorization або request body. Це свідома мінімізація даних;
клієнтські значення не включаються в шаблон події.

Браузер: register → login → profile → create → logout. До входу напис «Не ввійшли», профіль порожній.
Після входу Alice видно її id/userName/role; форма інциденту містить лише чотири дозволені поля.
Дата перетворюється в ISO UTC. Після logout профіль і результати очищено, повторний `/api/me` → 401.
Профіль виводиться через `textContent`; fetch обмежений same-origin. Паролі очищаються після спроби,
credentials/principal не зберігаються у localStorage/sessionStorage, cookie JavaScript не читає.
Console під час перевірки не містила записів. [Доказ браузерного сценарію](evidence/lr03/browser-flow.md).

## Межі безпеки

Це навчальний cookie-профіль, **CSRF-захист ще є навчальним боргом до ЛР6**. SameSite=Lax, HttpOnly
і CORS не замінюють повної CSRF-моделі. Перевірка доступу до вже існуючих чужих інцидентів — ЛР4.
Однакові повідомлення login не доводять однаковий час відповіді. Duplicate register лишає канал enumeration.
Перевірка duplicate через Identity не заявляється гарантією атомарності паралельних registration запитів.
Поглиблення рівня «Відмінно» не заявляються.

## Декларація та журнал ШІ

Під час виконання роботи я використовував **OpenAI Codex** для пояснення методички, допомоги
з реалізацією коду, підготовки й запуску тестів, перевірки браузерного сценарію та пояснення результатів.
Я обрав формат роботи й погодив контракт 400/409, обмеження входів і спосіб локального зберігання
навчального пароля. За покроковими поясненнями самостійно виконав демонстраційні дії, переглянув
відповіді API та результати тестів, зробив скріншоти й оформлював їх із поясненнями у Word-звіті.
Код, підготовлений за допомогою ШІ, перевірено збіркою, інтеграційними тестами та браузерним сценарієм.

| Дата | Сервіс | Завдання | Використано | Авторське рішення / перевірка |
|---|---|---|---|---|
| 21.10.2026 | OpenAI Codex | Продовжити ЛР2 за ЛР3 на «Добре» | Реалізація DTO, principal/owner, logout, UI, тести | Студент погодив 400/409, 10 входів/хв/IP; фактичні перевірки в evidence |
| 21–23.10.2026 | OpenAI Codex | Пояснити, що показати у звіті; допомогти з командами PowerShell | Локальний гайд, пояснення CP-01–03, уточнення команд | Студент самостійно пройшов гайд, виконав демонстраційні дії та зробив скріншоти |

## Подання

DEL-01: реалізація й тести; DEL-02: цей звіт, безпечні докази та окремий власний Word-звіт;
DEL-03: власний commit і тег `v0.3.0` після перевірки. Word, локальні паролі й персональний гайд
поза репозиторієм не додаються до Git. Перед поданням слід перевірити наявність гілки й тегу
в репозиторії lr3 та надати викладачеві доступ. Commit фінального стану визначається через тег v0.3.0.
