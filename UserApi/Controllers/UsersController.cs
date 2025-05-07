using Microsoft.AspNetCore.Mvc;
using UserApi.Models;
using UserApi.Services;

namespace UserApi.Controllers;

[ApiController]
[Route("api/[controller]")]
public class UsersController : ControllerBase
{
    private readonly UserService _userService;

    public UsersController(UserService userService)
    {
        _userService = userService;
    }

    /// <summary>
    /// Создание нового пользователя (только для админа)
    /// </summary>
    [HttpPost("create")]
    public IActionResult Create([FromBody] UserCreateRequest request)
    {
        // Авторизация через логин/пароль админа в заголовках запроса (упрощённо)
        if (!Request.Headers.TryGetValue("Login", out var loginHeader) ||
            !Request.Headers.TryGetValue("Password", out var passwordHeader))
        {
            return Unauthorized("Не переданы заголовки авторизации");
        }

        var admin = _userService.GetByLogin(loginHeader!);
        if (admin == null || admin.Password != passwordHeader || !admin.Admin)
            return StatusCode(403, "Недостаточно прав");

        if (_userService.GetByLogin(request.Login) != null)
            return Conflict("Пользователь с таким логином уже существует");

        var user = new User
        {
            Login = request.Login,
            Password = request.Password,
            Name = request.Name,
            Gender = request.Gender,
            Birthday = request.Birthday,
            Admin = request.Admin,
            CreatedBy = admin.Login
        };

        _userService.Add(user);

        return Ok(user);
    }
    [HttpPut("update-1/info")]
    public IActionResult UpdateInfo([FromBody] UserUpdateInfoRequest request)
    {
        // Авторизация
        if (!Request.Headers.TryGetValue("Login", out var loginHeader) ||
            !Request.Headers.TryGetValue("Password", out var passwordHeader))
        {
            return Unauthorized("Не переданы заголовки авторизации");
        }

        var sender = _userService.GetByLogin(loginHeader!);
        if (sender == null || sender.Password != passwordHeader)
            return StatusCode(403, "Неверные учетные данные");

        var target = _userService.GetByLogin(request.TargetLogin);
        if (target == null)
            return NotFound("Пользователь не найден");

        // Проверка прав
        var isSelf = sender.Login == target.Login;
        if (!sender.Admin && (!isSelf || target.RevokedOn != null))
            return StatusCode(403, "Нет прав на изменение");

        // Обновление данных
        target.Name = request.Name;
        target.Gender = request.Gender;
        target.Birthday = request.Birthday;
        target.ModifiedOn = DateTime.UtcNow;
        target.ModifiedBy = sender.Login;

        return Ok(target);
    }

    [HttpPut("update-1/password")]
    public IActionResult ChangePassword([FromBody] UserChangePasswordRequest request)
    {
        // Авторизация
        if (!Request.Headers.TryGetValue("Login", out var loginHeader) ||
            !Request.Headers.TryGetValue("Password", out var passwordHeader))
        {
            return Unauthorized("Не переданы заголовки авторизации");
        }

        var sender = _userService.GetByLogin(loginHeader!);
        if (sender == null || sender.Password != passwordHeader)
            return StatusCode(403, "Неверные учетные данные");

        var target = _userService.GetByLogin(request.TargetLogin);
        if (target == null)
            return NotFound("Пользователь не найден");

        var isSelf = sender.Login == target.Login;
        if (!sender.Admin && (!isSelf || target.RevokedOn != null))
            return StatusCode(403, "Нет прав на смену пароля");

        target.Password = request.NewPassword;
        target.ModifiedOn = DateTime.UtcNow;
        target.ModifiedBy = sender.Login;

        return Ok("Пароль успешно изменён");
    }
    [HttpPut("update-1/login")]
    public IActionResult ChangeLogin([FromBody] UserChangeLoginRequest request)
    {
        // Авторизация
        if (!Request.Headers.TryGetValue("Login", out var loginHeader) ||
            !Request.Headers.TryGetValue("Password", out var passwordHeader))
        {
            return Unauthorized("Не переданы заголовки авторизации");
        }

        var sender = _userService.GetByLogin(loginHeader!);
        if (sender == null || sender.Password != passwordHeader)
            return StatusCode(403, "Неверные учетные данные");

        var target = _userService.GetByLogin(request.TargetLogin);
        if (target == null)
            return NotFound("Пользователь не найден");

        if (_userService.GetByLogin(request.NewLogin) != null)
            return Conflict("Новый логин уже занят");

        var isSelf = sender.Login == target.Login;
        if (!sender.Admin && (!isSelf || target.RevokedOn != null))
            return StatusCode(403, "Нет прав на смену логина");

        target.Login = request.NewLogin;
        target.ModifiedOn = DateTime.UtcNow;
        target.ModifiedBy = sender.Login;

        return Ok("Логин успешно изменён");
    }

    [HttpGet("read/active")]
    public IActionResult GetActiveUsers()
    {
        // Авторизация
        if (!Request.Headers.TryGetValue("Login", out var loginHeader) ||
            !Request.Headers.TryGetValue("Password", out var passwordHeader))
        {
            return Unauthorized("Не переданы заголовки авторизации");
        }

        var sender = _userService.GetByLogin(loginHeader!);
        if (sender == null || sender.Password != passwordHeader || !sender.Admin)
            return StatusCode(403, "Доступ разрешён только администраторам");

        // Фильтрация и сортировка
        var users = _userService.GetAll()
            .Where(u => u.RevokedOn == null)
            .OrderBy(u => u.CreatedOn)
            .ToList();

        // Можно вернуть только нужные поля (если не хочешь показывать всё)
        var result = users.Select(u => new
        {
            u.Login,
            u.Name,
            u.Gender,
            u.Birthday,
            u.CreatedOn
        });

        return Ok(result);
    }


    [HttpGet("read/by-login/{login}")]
    public IActionResult GetUserByLogin(string login)
    {
        // Авторизация
        if (!Request.Headers.TryGetValue("Login", out var loginHeader) ||
            !Request.Headers.TryGetValue("Password", out var passwordHeader))
        {
            return Unauthorized("Не переданы заголовки авторизации");
        }

        var sender = _userService.GetByLogin(loginHeader!);
        if (sender == null || sender.Password != passwordHeader || !sender.Admin)
            return StatusCode(403, "Доступ разрешён только администраторам");

        var user = _userService.GetByLogin(login);
        if (user == null)
            return NotFound("Пользователь не найден");

        return Ok(new
        {
            user.Name,
            user.Gender,
            user.Birthday,
            IsActive = user.RevokedOn == null
        });
    }
    [HttpGet("read/self")]
    public IActionResult GetSelf()
    {
        // Авторизация через заголовки
        if (!Request.Headers.TryGetValue("Login", out var loginHeader) ||
            !Request.Headers.TryGetValue("Password", out var passwordHeader))
        {
            return Unauthorized("Не переданы заголовки авторизации");
        }

        var user = _userService.GetByLogin(loginHeader!);
        if (user == null || user.Password != passwordHeader)
            return StatusCode(403, "Неверные учетные данные");

        if (user.RevokedOn != null)
            return StatusCode(403, "Пользователь удалён");

        return Ok(new
        {
            user.Name,
            user.Gender,
            user.Birthday,
            IsActive = true
        });
    }


    [HttpGet("read/older-than/{age}")]
    public IActionResult GetUsersOlderThan(int age)
    {
        // Авторизация
        if (!Request.Headers.TryGetValue("Login", out var loginHeader) ||
            !Request.Headers.TryGetValue("Password", out var passwordHeader))
        {
            return Unauthorized("Не переданы заголовки авторизации");
        }

        var sender = _userService.GetByLogin(loginHeader!);
        if (sender == null || sender.Password != passwordHeader || !sender.Admin)
            return StatusCode(403, "Доступ разрешён только администраторам");

        var today = DateTime.UtcNow.Date;

        var users = _userService.GetAll()
            .Where(u => u.Birthday != null)
            .Where(u => u.RevokedOn == null)
            .Where(u =>
            {
                var birthDate = u.Birthday!.Value;
                var calculatedAge = today.Year - birthDate.Year;
                if (birthDate > today.AddYears(-calculatedAge)) calculatedAge--;
                return calculatedAge > age;
            })
            .Select(u => new
            {
                u.Login,
                u.Name,
                u.Birthday
            })
            .ToList();

        return Ok(users);
    }
    [HttpDelete("delete/{login}")]
    public IActionResult DeleteUser(string login, [FromBody] UserDeleteRequest request)
    {
        // Авторизация
        if (!Request.Headers.TryGetValue("Login", out var loginHeader) ||
            !Request.Headers.TryGetValue("Password", out var passwordHeader))
        {
            return Unauthorized("Не переданы заголовки авторизации");
        }

        var sender = _userService.GetByLogin(loginHeader!);
        if (sender == null || sender.Password != passwordHeader || !sender.Admin)
            return StatusCode(403, "Удаление доступно только администраторам");

        var target = _userService.GetByLogin(login);
        if (target == null)
            return NotFound("Пользователь не найден");

        // Защита от удаления самого себя
        if (target.Login == sender.Login)
            return StatusCode(403, "Нельзя удалить самого себя");

        if (request.SoftDelete)
        {
            if (target.RevokedOn != null)
                return Conflict("Пользователь уже удалён");

            target.RevokedOn = DateTime.UtcNow;
            target.RevokedBy = sender.Login;
            target.ModifiedOn = DateTime.UtcNow;
            target.ModifiedBy = sender.Login;

            return Ok("Пользователь мягко удалён");
        }
        else
        {
            _userService.Remove(target);
            return Ok("Пользователь полностью удалён");
        }
    }



    [HttpPut("restore/{login}")]
    public IActionResult RestoreUser(string login)
    {
        // Авторизация
        if (!Request.Headers.TryGetValue("Login", out var loginHeader) ||
            !Request.Headers.TryGetValue("Password", out var passwordHeader))
        {
            return Unauthorized("Не переданы заголовки авторизации");
        }

        var admin = _userService.GetByLogin(loginHeader!);
        if (admin == null || admin.Password != passwordHeader || !admin.Admin)
            return StatusCode(403, "Восстановление доступно только администраторам");


        var user = _userService.GetByLogin(login);
        if (user == null)
            return NotFound("Пользователь не найден");

        if (user.RevokedOn == null)
            return Conflict("Пользователь уже активен");

        user.RevokedOn = null;
        user.RevokedBy = null;
        user.ModifiedOn = DateTime.UtcNow;
        user.ModifiedBy = admin.Login;

        return Ok("Пользователь восстановлен");
    }


}
