# Фактичні перевірки ЛР3 — 21.10.2026

- .NET SDK 10.0.402, `net10.0`, гілка `lab/3-authentication`.
- Scaffold lab-03-start-v1 від v0.2.0 / 00bf3ec.
- Locked restore успішний. [Фінальна збірка](build-final.txt): 0 помилок, 0 попереджень.
- [Reset №1](reset-1.txt) → [усі 68 тестів пройдено](tests-reset-1.txt), 0 failed, 0 skipped.
- [Reset №2](reset-2.txt) → [усі 68 тестів пройдено](tests-reset-2.txt), 0 failed, 0 skipped.
- Після другого набору тестів у БД: 4 Identity users, 3 roles, 5 incidents.
- Тести реєстрації повторно пройшли після кожного reset; створені ними акаунти очищаються.
- До виправлення /api/me довіряв header Bob: [200](before-me.txt). Після: [401](after-me.txt).
- [Схема identity_users без значень](identity-schema.txt).

## Відповідність вимогам

| Вимога | Перевірка | Фактичний результат |
|---|---|---|
| T-01 | RegistrationSecurityTests.T01 | 201, тільки safe DTO, серверні Id/Reporter, без автоматичного входу |
| T-02 | T02 + Duplicate | 400 для невалідних полів; однаковий 409 для повторного name/email |
| T-03 | IdentitySeedTests + схема | Identity verifier приймає локальний пароль; представлення різні; значення не друкуються |
| A-01 | AuthenticationSecurityTests.A01 | GET me і POST incidents без cookie → 401, без redirect/Bearer |
| A-02 | A02 | cookie Alice → id Alice, дозволені поля |
| A-03 | A03 | cookie Alice + заголовок Bob → Alice |
| A-04 | A04_A05(false) | Bob без owner у JSON → власник Bob |
| A-05 | A04_A05(true) | Bob із forged owner Alice → власник Bob |
| A-06 | A06 | logout → me і create дають 401 тим самим клієнтом |
| A-07 | A07 | невідомий user / неправильний пароль → однаковий зовнішній контракт |
| Cookie flags | CookieFlags: 3 профілі | HttpOnly/Lax, Secure у HTTPS; session cookie; 14 днів/sliding |
| Login limit | LoginLimit | 10 прийнятих спроб, 429, після 62 секунд — 204; квота спільна для клієнтів host |
| S-01 / події | AuthenticationLoggingTests | лише EventType/UtcTime/Result/TraceId, без credentials |
| Browser flow | [Окремий протокол](browser-flow.md) | register/login/profile/create/logout перевірено в браузері |
| Попередні ЛР | збережені 42 регресії | усі проходять, POST ЛР2 через реальний cookie-сеанс |

43 початкові тести scaffold + 25 нових випадків = 68. Повний запуск займає близько 1 хв 9 с,
оскільки rate limit перевіряється зі справжнім хвилинним вікном, без тестового зменшення.
Тести A-04 та A-05 представлені двома випадками однієї theory.

Докази містять лише вибрані безпечні підсумки. Повні заголовки cookie, password/hash, дампи й connection string
не включені. Зміни не закомічено, фінальний тег не створено: ці дії виконує студент.
