 # User API

Приложение на ASP.NET Core для управления пользователями.  
Данные хранятся в памяти и сбрасываются при перезапуске.

## Возможности

- Авторизация через заголовки `Login` и `Password`
- Создание, редактирование, удаление и восстановление пользователей
- Просмотр активных, по логину, себя и по возрасту
- Мягкое и полное удаление
- Swagger UI для тестирования

## Запуск

1. Открыть проект в Visual Studio  
2. Нажать F5 или запустить `dotnet run`  
3. Перейти в Swagger: `https://localhost:<порт>/swagger`

## Примеры маршрутов

- `POST /api/users/create-user`  
- `PUT /api/users/update-1/info`  
- `GET /api/users/read/active`  
- `DELETE /api/users/delete/{login}`  
- `PUT /api/users/restore/{login}`

## Заголовки для авторизации

- `Login: Admin`  
- `Password: Admin123`
