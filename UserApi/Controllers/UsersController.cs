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


}
