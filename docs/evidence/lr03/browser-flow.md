# Браузерна перевірка — 21.10.2026

Локальна сторінка http://localhost:5080/auth-lab.html, той самий браузерний сеанс.

| Крок | Спостереження | Доказ |
|---|---|---|
| До входу | «Не ввійшли», профіль відсутній, create disabled | Візуальна перевірка початкового стану |
| Login Alice | 204, профіль 200, id …0001, Reporter; поле пароля очищене | [Скрін](browser-alice.png) |
| Create | 201, New, час у UTC; request у коді має тільки title/description/severity/occurredAtUtc | [Скрін](browser-create.png) |
| Logout | 204, профіль прибрано, повторний GET /api/me → 401 | [Скрін](browser-logout.png) |
| Register | 201, safe поля; стан залишається анонімним | [Скрін](browser-register.png) |
| Duplicate register | 409, без уточнення зайнятого поля | [Скрін](browser-duplicate.png) |
| Browser credentials boundary | auth-lab.js не використовує localStorage/sessionStorage або console; дозволені поля через textContent | Перегляд коду; браузерний журнал warn/error/log порожній під час сценарію |

Значення cookie та пароль не показувалися. Перевірка відсутності записів storage у цьому протоколі є
оглядом коду (без твердження, що весь профіль браузера порожній). Після сценарію два reset прибрали
створений тестовий акаунт та інцидент. Повторний ручний показ створить нові записи.
