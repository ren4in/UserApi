using Xunit;
using Moq;
using Microsoft.AspNetCore.Mvc;
using UserApi.Controllers;
using UserApi.Models;
using UserApi.Services;
using UserApi.Services.UserApi.Services;
using System.Text.Json;

namespace UserApi.Tests.Controllers;

public class UsersControllerTests
{
    private readonly Mock<IUserService> _mockService;

    // Вспомогательный контроллер с возможностью задать "текущего" пользователя
    private class TestableUsersController : UsersController
    {
        private readonly User? _fakeUser;

        public TestableUsersController(IUserService service, User? fakeUser)
            : base(service)
        {
            _fakeUser = fakeUser;
        }

        protected override User? GetCurrentUser() => _fakeUser;
    }

    public UsersControllerTests()
    {
        _mockService = new Mock<IUserService>();
    }

    //null в теле запроса
    [Fact]
    public void Create_ShouldReturnBadRequest_IfRequestIsNull()
    {
        // Arrange
        var controller = new TestableUsersController(_mockService.Object, new User());

        // Act
        var result = controller.Create(null);

        // Assert
        var badRequest = Assert.IsType<BadRequestObjectResult>(result);
        Assert.Equal("Запрос не содержит данных. Проверьте тело запроса.", badRequest.Value);
    }

    //  Логин уже существует
    [Fact]
    public void Create_ShouldReturnConflict_IfLoginExists()
    {
        // Arrange
        var request = new UserCreateRequest { Login = "existingUser" };
        _mockService.Setup(s => s.GetByLogin("existingUser")).Returns(new User());

        var controller = new TestableUsersController(_mockService.Object, new User());

        // Act
        var result = controller.Create(request);

        // Assert
        var conflict = Assert.IsType<ConflictObjectResult>(result);
        Assert.Equal("Пользователь с логином 'existingUser' уже существует.", conflict.Value);
    }

    //  Нет авторизованного администратора
    [Fact]
    public void Create_ShouldReturnUnauthorized_IfAdminIsNull()
    {
        // Arrange
        var request = new UserCreateRequest
        {
            Login = "newUser",
            Password = "pass",
            Name = "Test",
            Gender = 0,
            Birthday = new DateTime(2000, 1, 1),
            Admin = false
        };

        _mockService.Setup(s => s.GetByLogin("newUser")).Returns((User)null);

        var controller = new TestableUsersController(_mockService.Object, null); // без текущего пользователя

        // Act
        var result = controller.Create(request);

        // Assert
        var unauthorized = Assert.IsType<UnauthorizedObjectResult>(result);
        Assert.Equal("Токен недействителен или не найден. Повторите авторизацию.", unauthorized.Value);
    }

    // Успешное создание пользователя
    [Fact]
       public void Create_ShouldReturnOk_IfUserCreatedSuccessfully()
    {
        // Arrange
        var request = new UserCreateRequest
        {
            Login = "newUser",
            Password = "pass",
            Name = "Test",
            Gender = 0, // Female
            Birthday = new DateTime(1990, 5, 5),
            Admin = true
        };

        var admin = new User { Login = "admin" };

        _mockService.Setup(s => s.GetByLogin("newUser")).Returns((User)null);
        _mockService.Setup(s => s.Add(It.IsAny<User>())).Verifiable();

        var controller = new TestableUsersController(_mockService.Object, admin);

        // Act
        var result = controller.Create(request);

        // Assert
        var okResult = Assert.IsType<OkObjectResult>(result);
        var json = JsonSerializer.Serialize(okResult.Value); // using System.Text.Json
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        Assert.Equal("Пользователь успешно создан.", root.GetProperty("message").GetString());

        var user = root.GetProperty("createdUser");
        Assert.Equal("newUser", user.GetProperty("Login").GetString());
        Assert.Equal("Test", user.GetProperty("Name").GetString());
        Assert.Equal(0, user.GetProperty("Gender").GetInt32());
        Assert.StartsWith("1990-05-05", user.GetProperty("Birthday").GetString());
        Assert.True(user.GetProperty("Admin").GetBoolean());

        _mockService.Verify(s => s.Add(It.IsAny<User>()), Times.Once);
    }
    // Тест 1: Возврат Unauthorized, если пользователь не авторизован
    [Fact]
    public void UpdateInfo_ReturnsUnauthorized_WhenUserIsNotAuthenticated()
    {
        // Arrange
        var request = new UserUpdateInfoRequest { TargetLogin = "user" };
        var controller = new TestableUsersController(_mockService.Object, null); // нет текущего пользователя

        // Act
        var result = controller.UpdateInfo(request);

        // Assert
        var unauthorizedResult = Assert.IsType<UnauthorizedObjectResult>(result);
        Assert.Equal("Вы не авторизованы или токен недействителен.", unauthorizedResult.Value);
    }

    // Возврат NotFound, если целевой пользователь не найден
    [Fact]
    public void UpdateInfo_ReturnsNotFound_WhenTargetUserDoesNotExist()
    {
        // Arrange
        var currentUser = new User { Login = "admin", Admin = true };
        var request = new UserUpdateInfoRequest { TargetLogin = "missingUser" };

        _mockService.Setup(s => s.GetByLogin("missingUser")).Returns((User)null);
        var controller = new TestableUsersController(_mockService.Object, currentUser);

        // Act
        var result = controller.UpdateInfo(request);

        // Assert
        var notFoundResult = Assert.IsType<NotFoundObjectResult>(result);
        Assert.Equal("Пользователь с логином 'missingUser' не найден.", notFoundResult.Value);
    }

    // Возврат Forbid, если неадмин пытается изменить чужие данные
    [Fact]
    public void UpdateInfo_ReturnsForbid_WhenNonAdminModifiesAnotherUser()
    {
        // Arrange
        var currentUser = new User { Login = "user1", Admin = false };
        var targetUser = new User { Login = "user2" };

        var request = new UserUpdateInfoRequest
        {
            TargetLogin = "user2",
            Name = "ChangedName",
            Gender = 1,
            Birthday = new DateTime(2000, 1, 1)
        };

        _mockService.Setup(s => s.GetByLogin("user2")).Returns(targetUser);
        var controller = new TestableUsersController(_mockService.Object, currentUser);

        // Act
        var result = controller.UpdateInfo(request);

        // Assert
        Assert.IsType<ForbidResult>(result);
    }
    // Возврат 403 и сообщение, если пользователь неадмин и удалён

    [Fact]
    public void UpdateInfo_ReturnsForbid_WhenUserIsDeletedAndNotAdmin()
    {
        // Arrange
        var currentUser = new User { Login = "user1", Admin = false };
        var deletedUser = new User { Login = "user1", RevokedOn = DateTime.UtcNow };

        var request = new UserUpdateInfoRequest
        {
            TargetLogin = "user1",
            Name = "ChangedName",
            Gender = 1,
            Birthday = new DateTime(2000, 1, 1)
        };

        _mockService.Setup(s => s.GetByLogin("user1")).Returns(deletedUser);
        var controller = new TestableUsersController(_mockService.Object, currentUser);

        // Act
        var result = controller.UpdateInfo(request);

        // Assert
        Assert.IsType<ForbidResult>(result); // без сообщения
    }

    // Успешное обновление информации админом
    [Fact]
    public void UpdateInfo_ReturnsOk_WhenAdminUpdatesUserSuccessfully()
    {
        // Arrange
        var adminUser = new User { Login = "admin", Admin = true };
        var targetUser = new User { Login = "user2", Name = "OldName" };

        var request = new UserUpdateInfoRequest
        {
            TargetLogin = "user2",
            Name = "NewName",
            Gender = 0,
            Birthday = new DateTime(1990, 1, 1)
        };

        _mockService.Setup(s => s.GetByLogin("user2")).Returns(targetUser);
        var controller = new TestableUsersController(_mockService.Object, adminUser);

        // Act
        var result = controller.UpdateInfo(request);

        // Assert
        var okResult = Assert.IsType<OkObjectResult>(result);
        var json = JsonSerializer.Serialize(okResult.Value);
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;

        Assert.Equal("Информация успешно обновлена.", root.GetProperty("message").GetString());

        var updated = root.GetProperty("updated");
        Assert.Equal("user2", updated.GetProperty("Login").GetString());
        Assert.Equal("NewName", updated.GetProperty("Name").GetString());
        Assert.Equal(0, updated.GetProperty("Gender").GetInt32());
        Assert.StartsWith("1990-01-01", updated.GetProperty("Birthday").GetString());
    }

    // Возврат Unauthorized, если пользователь не авторизован
    [Fact]
    public void ChangePassword_ReturnsUnauthorized_WhenUserIsNotAuthenticated()
    {
        // Arrange
        var request = new UserChangePasswordRequest { TargetLogin = "user1", NewPassword = "newpass" };
        var controller = new TestableUsersController(_mockService.Object, null);

        // Act
        var result = controller.ChangePassword(request);

        // Assert
        var unauthorized = Assert.IsType<UnauthorizedObjectResult>(result);
        Assert.Equal("Вы не вошли в систему или токен недействителен.", unauthorized.Value);
    }

    // Возврат NotFound, если целевой пользователь не найден
    [Fact]
    public void ChangePassword_ReturnsNotFound_WhenTargetUserDoesNotExist()
    {
        // Arrange
        var currentUser = new User { Login = "admin", Admin = true };
        var request = new UserChangePasswordRequest { TargetLogin = "missing", NewPassword = "newpass" };

        _mockService.Setup(s => s.GetByLogin("missing")).Returns((User)null);
        var controller = new TestableUsersController(_mockService.Object, currentUser);

        // Act
        var result = controller.ChangePassword(request);

        // Assert
        var notFound = Assert.IsType<NotFoundObjectResult>(result);
        Assert.Equal("Пользователь с логином 'missing' не найден.", notFound.Value);
    }

    //  Forbid, если неадмин пытается изменить пароль другого пользователя
    [Fact]
       public void ChangePassword_ReturnsForbid_WhenNonAdminChangesOthersPassword()
    {
        // Arrange
        var currentUser = new User { Login = "user1", Admin = false };
        var targetUser = new User { Login = "user2" }; // другой пользователь

        var request = new UserChangePasswordRequest
        {
            TargetLogin = "user2",
            NewPassword = "newpass"
        };

        _mockService.Setup(s => s.GetByLogin("user2")).Returns(targetUser);
        var controller = new TestableUsersController(_mockService.Object, currentUser);

        // Act
        var result = controller.ChangePassword(request);

        // Assert
        var forbid = Assert.IsType<ObjectResult>(result);
        Assert.Equal(403, forbid.StatusCode);
        Assert.Equal("У вас нет прав на изменение пароля другого пользователя.", forbid.Value);
    }


    // Forbid, если неадмин пытается изменить пароль удалённого пользователя (даже если сам)
    [Fact]
    public void ChangePassword_ReturnsForbid_WhenUserIsDeletedAndNotAdmin()
    {
        // Arrange
        var currentUser = new User { Login = "user1", Admin = false };
        var deletedUser = new User { Login = "user1", RevokedOn = DateTime.UtcNow };

        var request = new UserChangePasswordRequest
        {
            TargetLogin = "user1",
            NewPassword = "newpass"
        };

        _mockService.Setup(s => s.GetByLogin("user1")).Returns(deletedUser);
        var controller = new TestableUsersController(_mockService.Object, currentUser);

        // Act
        var result = controller.ChangePassword(request);

        // Assert
        var forbid = Assert.IsType<ObjectResult>(result);
        Assert.Equal(403, forbid.StatusCode);
        Assert.Equal("Невозможно изменить пароль удалённого пользователя.", forbid.Value);
    }

    // Успешная смена пароля админом
    [Fact]
    public void ChangePassword_ReturnsOk_WhenAdminChangesPassword()
    {
        // Arrange
        var adminUser = new User { Login = "admin", Admin = true };
        var targetUser = new User { Login = "user2", Password = "oldpass" };

        var request = new UserChangePasswordRequest
        {
            TargetLogin = "user2",
            NewPassword = "newpass"
        };

        _mockService.Setup(s => s.GetByLogin("user2")).Returns(targetUser);
        var controller = new TestableUsersController(_mockService.Object, adminUser);

        // Act
        var result = controller.ChangePassword(request);

        // Assert
        var ok = Assert.IsType<OkObjectResult>(result);
        Assert.Equal("Пароль успешно изменён.", ok.Value);
        Assert.Equal("newpass", targetUser.Password);
        Assert.Equal("admin", targetUser.ModifiedBy);
        Assert.True(targetUser.ModifiedOn.HasValue);
    }

    // Успешная смена пароля самим пользователем
    [Fact]
    public void ChangePassword_ReturnsOk_WhenUserChangesOwnPassword()
    {
        // Arrange
        var user = new User { Login = "user1", Admin = false, Password = "oldpass" };

        var request = new UserChangePasswordRequest
        {
            TargetLogin = "user1",
            NewPassword = "myNewPass"
        };

        _mockService.Setup(s => s.GetByLogin("user1")).Returns(user);
        var controller = new TestableUsersController(_mockService.Object, user);

        // Act
        var result = controller.ChangePassword(request);

        // Assert
        var ok = Assert.IsType<OkObjectResult>(result);
        Assert.Equal("Пароль успешно изменён.", ok.Value);
        Assert.Equal("myNewPass", user.Password);
        Assert.Equal("user1", user.ModifiedBy);
        Assert.True(user.ModifiedOn.HasValue);
    }

    //  Возвращает BadRequest, если тело запроса null
    [Fact]
    public void ChangeLogin_ReturnsBadRequest_WhenRequestIsNull()
    {
        var controller = new TestableUsersController(_mockService.Object, new User());
        var result = controller.ChangeLogin(null);

        var badRequest = Assert.IsType<BadRequestObjectResult>(result);
        Assert.Equal("Запрос не содержит данных. Проверьте тело запроса.", badRequest.Value);
    }

    // Возвращает BadRequest, если новый логин пустой
    [Fact]
    public void ChangeLogin_ReturnsBadRequest_WhenNewLoginIsEmpty()
    {
        var controller = new TestableUsersController(_mockService.Object, new User());
        var request = new UserChangeLoginRequest { NewLogin = "   " };

        var result = controller.ChangeLogin(request);

        var badRequest = Assert.IsType<BadRequestObjectResult>(result);
        Assert.Equal("Новый логин не может быть пустым.", badRequest.Value);
    }

    // Возвращает Unauthorized, если пользователь не авторизован
    [Fact]
    public void ChangeLogin_ReturnsUnauthorized_WhenUserNotAuthenticated()
    {
        var controller = new TestableUsersController(_mockService.Object, null);
        var request = new UserChangeLoginRequest { TargetLogin = "user1", NewLogin = "newlogin" };

        var result = controller.ChangeLogin(request);

        var unauthorized = Assert.IsType<UnauthorizedObjectResult>(result);
        Assert.Equal("Вы не авторизованы или токен недействителен.", unauthorized.Value);
    }

    // Возвращает NotFound, если целевой пользователь не найден
    [Fact]
    public void ChangeLogin_ReturnsNotFound_WhenTargetUserNotFound()
    {
        var sender = new User { Login = "admin", Admin = true };
        var request = new UserChangeLoginRequest { TargetLogin = "missingUser", NewLogin = "newlogin" };

        _mockService.Setup(s => s.GetByLogin("missingUser")).Returns((User)null);

        var controller = new TestableUsersController(_mockService.Object, sender);
        var result = controller.ChangeLogin(request);

        var notFound = Assert.IsType<NotFoundObjectResult>(result);
        Assert.Equal("Пользователь с логином 'missingUser' не найден.", notFound.Value);
    }

    // ТВозвращает Conflict, если новый логин уже занят
    [Fact]
    public void ChangeLogin_ReturnsConflict_WhenNewLoginAlreadyExists()
    {
        var sender = new User { Login = "admin", Admin = true };
        var existingUser = new User { Login = "existing" };
        var targetUser = new User { Login = "user1" };

        var request = new UserChangeLoginRequest { TargetLogin = "user1", NewLogin = "existing" };

        _mockService.Setup(s => s.GetByLogin("user1")).Returns(targetUser);
        _mockService.Setup(s => s.GetByLogin("existing")).Returns(existingUser);

        var controller = new TestableUsersController(_mockService.Object, sender);
        var result = controller.ChangeLogin(request);

        var conflict = Assert.IsType<ConflictObjectResult>(result);
        Assert.Equal("Логин 'existing' уже занят другим пользователем.", conflict.Value);
    }

    // Возвращает Forbid, если пользователь не админ и пытается изменить чужой логин
    [Fact]
    public void ChangeLogin_ReturnsForbid_WhenNonAdminTriesToChangeOthersLogin()
    {
        var sender = new User { Login = "user1", Admin = false };
        var target = new User { Login = "user2" };

        var request = new UserChangeLoginRequest { TargetLogin = "user2", NewLogin = "newlogin" };

        _mockService.Setup(s => s.GetByLogin("user2")).Returns(target);
        _mockService.Setup(s => s.GetByLogin("newlogin")).Returns((User)null);

        var controller = new TestableUsersController(_mockService.Object, sender);
        var result = controller.ChangeLogin(request);

        var forbid = Assert.IsType<ObjectResult>(result);
        Assert.Equal(403, forbid.StatusCode);
        Assert.Equal("Вы можете изменять логин только для себя.", forbid.Value);
    }

    // Возвращает Forbid, если пользователь не админ и удалён
    [Fact]
    public void ChangeLogin_ReturnsForbid_WhenUserIsDeletedAndNotAdmin()
    {
        var sender = new User { Login = "user1", Admin = false };
        var deleted = new User { Login = "user1", RevokedOn = DateTime.UtcNow };

        var request = new UserChangeLoginRequest { TargetLogin = "user1", NewLogin = "newlogin" };

        _mockService.Setup(s => s.GetByLogin("user1")).Returns(deleted);
        _mockService.Setup(s => s.GetByLogin("newlogin")).Returns((User)null);

        var controller = new TestableUsersController(_mockService.Object, sender);
        var result = controller.ChangeLogin(request);

        var forbid = Assert.IsType<ObjectResult>(result);
        Assert.Equal(403, forbid.StatusCode);
        Assert.Equal("Нельзя изменить логин удалённого пользователя.", forbid.Value);
    }

    // Успешное изменение логина админом
    [Fact]
    public void ChangeLogin_ReturnsOk_WhenAdminChangesLoginSuccessfully()
    {
        var sender = new User { Login = "admin", Admin = true };
        var target = new User { Login = "user1" };

        var request = new UserChangeLoginRequest { TargetLogin = "user1", NewLogin = "newlogin" };

        _mockService.Setup(s => s.GetByLogin("user1")).Returns(target);
        _mockService.Setup(s => s.GetByLogin("newlogin")).Returns((User)null);

        var controller = new TestableUsersController(_mockService.Object, sender);
        var result = controller.ChangeLogin(request);

        var ok = Assert.IsType<OkObjectResult>(result);
        var json = JsonSerializer.Serialize(ok.Value);
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        Assert.Equal("Логин успешно изменён.", root.GetProperty("message").GetString());

        var updatedUser = root.GetProperty("updatedUser");
        Assert.Equal("user1", updatedUser.GetProperty("oldLogin").GetString());
        Assert.Equal("newlogin", updatedUser.GetProperty("newLogin").GetString());
    }

    //  Возвращает сообщение, если активных пользователей нет
    [Fact]
    public void GetActiveUsers_ReturnsMessage_WhenNoActiveUsers()
    {
        _mockService.Setup(s => s.GetAll()).Returns(new List<User>
    {
        new User { Login = "old1", RevokedOn = DateTime.UtcNow },
        new User { Login = "old2", RevokedOn = DateTime.UtcNow }
    });

        var controller = new TestableUsersController(_mockService.Object, new User { Admin = true });

        var result = controller.GetActiveUsers();

        var ok = Assert.IsType<OkObjectResult>(result);
        var json = JsonSerializer.Serialize(ok.Value);
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        Assert.Equal("Нет активных пользователей.", root.GetProperty("message").GetString());
    }

    // Возвращает список активных пользователей с корректным сообщением
    [Fact]
    public void GetActiveUsers_ReturnsList_WhenActiveUsersExist()
    {
        _mockService.Setup(s => s.GetAll()).Returns(new List<User>
    {
        new User { Login = "user1", Name = "Test1", Gender = 1, CreatedOn = DateTime.UtcNow.AddDays(-2) },
        new User { Login = "user2", Name = "Test2", Gender = 0, CreatedOn = DateTime.UtcNow }
    });

        var controller = new TestableUsersController(_mockService.Object, new User { Admin = true });

     
        var result = controller.GetActiveUsers();

     
        var ok = Assert.IsType<OkObjectResult>(result);
        var json = JsonSerializer.Serialize(ok.Value);
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        Assert.StartsWith("Найдено активных пользователей: ", root.GetProperty("message").GetString());

        var users = root.GetProperty("users");
        Assert.Equal(2, users.GetArrayLength());

        Assert.Equal("user1", users[0].GetProperty("Login").GetString()); // проверяем порядок
        Assert.Equal("user2", users[1].GetProperty("Login").GetString());
    }

    // Тест 1: Возвращает BadRequest, если логин пустой
    [Fact]
    public void GetUserByLogin_ReturnsBadRequest_WhenLoginIsEmpty()
    {
      
        var controller = new TestableUsersController(_mockService.Object, new User { Admin = true });

      
        var result = controller.GetUserByLogin("  "); // пустой логин

     
        var badRequest = Assert.IsType<BadRequestObjectResult>(result);
        Assert.Equal("Логин не указан.", badRequest.Value);
    }

    // Возвращает NotFound, если пользователь не найден
    [Fact]
    public void GetUserByLogin_ReturnsNotFound_WhenUserDoesNotExist()
    {
       
        var controller = new TestableUsersController(_mockService.Object, new User { Admin = true });

        _mockService.Setup(s => s.GetByLogin("ghost")).Returns((User)null);

        var result = controller.GetUserByLogin("ghost");

 
        var notFound = Assert.IsType<NotFoundObjectResult>(result);
        Assert.Equal("Пользователь с логином 'ghost' не найден.", notFound.Value);
    }

    // Возвращает информацию об активном пользователе
    [Fact]
    public void GetUserByLogin_ReturnsUserInfo_WhenUserIsActive()
    {
       
        var user = new User
        {
            Login = "user1",
            Name = "Test User",
            Gender = 1,
            Birthday = new DateTime(2000, 1, 1),
            RevokedOn = null
        };

        _mockService.Setup(s => s.GetByLogin("user1")).Returns(user);
        var controller = new TestableUsersController(_mockService.Object, new User { Admin = true });

        
        var result = controller.GetUserByLogin("user1");

        
        var ok = Assert.IsType<OkObjectResult>(result);
        var json = JsonSerializer.Serialize(ok.Value);
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        Assert.Equal("Test User", root.GetProperty("Name").GetString());
        Assert.Equal(1, root.GetProperty("Gender").GetInt32());
        Assert.StartsWith("2000-01-01", root.GetProperty("Birthday").GetString());
        Assert.True(root.GetProperty("IsActive").GetBoolean());
    }

    // Возвращает информацию об удалённом пользователе
    [Fact]
    public void GetUserByLogin_ReturnsUserInfo_WhenUserIsDeleted()
    {
        var user = new User
        {
            Login = "deletedUser",
            Name = "Old User",
            Gender = 0,
            Birthday = new DateTime(1995, 5, 5),
            RevokedOn = DateTime.UtcNow
        };

        _mockService.Setup(s => s.GetByLogin("deletedUser")).Returns(user);
        var controller = new TestableUsersController(_mockService.Object, new User { Admin = true });

       
        var result = controller.GetUserByLogin("deletedUser");

       
        var ok = Assert.IsType<OkObjectResult>(result);
        var json = JsonSerializer.Serialize(ok.Value);
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        Assert.Equal("Old User", root.GetProperty("Name").GetString());
        Assert.False(root.GetProperty("IsActive").GetBoolean());
    }


    // Возвращает Unauthorized, если текущий пользователь не авторизован
    [Fact]
    public void GetSelf_ReturnsUnauthorized_WhenUserIsNull()
    {
        var controller = new TestableUsersController(_mockService.Object, null);

        var result = controller.GetSelf();

        var unauthorized = Assert.IsType<UnauthorizedObjectResult>(result);
        Assert.Equal("Вы не авторизованы или токен недействителен.", unauthorized.Value);
    }

    // Возвращает Forbid, если пользователь был удалён
    [Fact]
    public void GetSelf_ReturnsForbid_WhenUserIsDeleted()
    {
        var deletedUser = new User
        {
            Login = "user1",
            RevokedOn = DateTime.UtcNow
        };

        var controller = new TestableUsersController(_mockService.Object, deletedUser);

        var result = controller.GetSelf();

        var forbid = Assert.IsType<ForbidResult>(result);
    }

    // Возвращает информацию о текущем пользователе, если он активен
    [Fact]
    public void GetSelf_ReturnsUserInfo_WhenUserIsActive()
    {
        var user = new User
        {
            Login = "user1",
            Name = "Test User",
            Gender = 1,
            Birthday = new DateTime(1990, 1, 1),
            RevokedOn = null
        };

        var controller = new TestableUsersController(_mockService.Object, user);

        var result = controller.GetSelf();

        var ok = Assert.IsType<OkObjectResult>(result);
        var json = JsonSerializer.Serialize(ok.Value);
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        Assert.Equal("user1", root.GetProperty("Login").GetString());
        Assert.Equal("Test User", root.GetProperty("Name").GetString());
        Assert.Equal(1, root.GetProperty("Gender").GetInt32());
        Assert.StartsWith("1990-01-01", root.GetProperty("Birthday").GetString());
        Assert.True(root.GetProperty("IsActive").GetBoolean());
    }


    // Возвращает BadRequest, если возраст меньше 0
    [Fact]
    public void GetUsersOlderThan_ReturnsBadRequest_WhenAgeIsNegative()
    {
        var controller = new TestableUsersController(_mockService.Object, new User { Admin = true });

        var result = controller.GetUsersOlderThan(-1);

        var badRequest = Assert.IsType<BadRequestObjectResult>(result);
        Assert.Equal("Возраст должен быть в пределах от 0 до 150.", badRequest.Value);
    }

    // Возвращает BadRequest, если возраст больше 150
    [Fact]
    public void GetUsersOlderThan_ReturnsBadRequest_WhenAgeIsTooHigh()
    {
        var controller = new TestableUsersController(_mockService.Object, new User { Admin = true });

        var result = controller.GetUsersOlderThan(200);

        var badRequest = Assert.IsType<BadRequestObjectResult>(result);
        Assert.Equal("Возраст должен быть в пределах от 0 до 150.", badRequest.Value);
    }

    // Возвращает сообщение, если нет подходящих пользователей
    [Fact]
    public void GetUsersOlderThan_ReturnsMessage_WhenNoMatchingUsers()
    {
        var today = DateTime.UtcNow.Date;
        _mockService.Setup(s => s.GetAll()).Returns(new List<User>
    {
        new User { Login = "young", Birthday = today.AddYears(-20), RevokedOn = null }
    });

        var controller = new TestableUsersController(_mockService.Object, new User { Admin = true });

        var result = controller.GetUsersOlderThan(100);

        var ok = Assert.IsType<OkObjectResult>(result);
        var json = JsonSerializer.Serialize(ok.Value);
        using var doc = JsonDocument.Parse(json);
        Assert.Equal("Нет пользователей старше 100 лет.", doc.RootElement.GetProperty("message").GetString());
    }

    // Возвращает список пользователей старше указанного возраста
    [Fact]
    public void GetUsersOlderThan_ReturnsUsers_WhenMatchesExist()
    {
        var today = DateTime.UtcNow.Date;
        _mockService.Setup(s => s.GetAll()).Returns(new List<User>
    {
        new User { Login = "oldie", Name = "Old User", Birthday = today.AddYears(-70), RevokedOn = null },
        new User { Login = "young", Name = "Young User", Birthday = today.AddYears(-20), RevokedOn = null }
    });

        var controller = new TestableUsersController(_mockService.Object, new User { Admin = true });

        var result = controller.GetUsersOlderThan(60);

        var ok = Assert.IsType<OkObjectResult>(result);
        var json = JsonSerializer.Serialize(ok.Value);
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        Assert.Equal("Найдено пользователей старше 60 лет: 1", root.GetProperty("message").GetString());

        var users = root.GetProperty("users");
        Assert.Equal(1, doc.RootElement.GetProperty("users").GetArrayLength());
        Assert.Equal("oldie", users[0].GetProperty("Login").GetString());
        Assert.Equal("Old User", users[0].GetProperty("Name").GetString());
    }


    // Возвращает BadRequest, если логин пустой
    [Fact]
        public void DeleteUser_ReturnsBadRequest_WhenLoginIsEmpty()
        {
            var controller = new TestableUsersController(_mockService.Object, new User { Admin = true });

            var result = controller.DeleteUser(" ", new UserDeleteRequest { SoftDelete = true }); // или false, в зависимости от сценария


            var badRequest = Assert.IsType<BadRequestObjectResult>(result);
        Assert.Equal("Логин пользователя не указан.", badRequest.Value);
    }
    // Возвращает NotFound, если пользователь не найден
    [Fact]
    public void DeleteUser_ReturnsNotFound_WhenUserDoesNotExist()
    {
        var login = "ghost";
        _mockService.Setup(s => s.GetByLogin(login)).Returns((User)null);
        var controller = new TestableUsersController(_mockService.Object, new User { Admin = true });

        var result = controller.DeleteUser(login, new UserDeleteRequest { SoftDelete = true });

        var notFound = Assert.IsType<NotFoundObjectResult>(result);
        Assert.Equal($"Пользователь с логином '{login}' не найден.", notFound.Value);
    }

    // Возвращает Conflict, если пользователь уже удалён
    [Fact]
    public void DeleteUser_ReturnsConflict_WhenUserAlreadyDeleted()
    {
        var login = "deleted";
        var deletedUser = new User { Login = login, RevokedOn = DateTime.UtcNow };

        _mockService.Setup(s => s.GetByLogin(login)).Returns(deletedUser);
        var controller = new TestableUsersController(_mockService.Object, new User { Admin = true });

        var result = controller.DeleteUser(login, new UserDeleteRequest { SoftDelete = true });

        var conflict = Assert.IsType<ConflictObjectResult>(result);
        Assert.Equal($"Пользователь '{login}' уже был удалён ранее.", conflict.Value);
    }

    // Успешно мягко удаляет пользователя и возвращает Ok
    [Fact]
    public void DeleteUser_ReturnsOk_WhenUserIsActive()
    {
        var login = "user1";
        var targetUser = new User { Login = login, RevokedOn = null };
        var adminUser = new User { Login = "admin", Admin = true };

        _mockService.Setup(s => s.GetByLogin(login)).Returns(targetUser);
        var controller = new TestableUsersController(_mockService.Object, adminUser);

        var result = controller.DeleteUser(login, new UserDeleteRequest { SoftDelete = true });

        var ok = Assert.IsType<OkObjectResult>(result);
        var json = JsonSerializer.Serialize(ok.Value);
        using var doc = JsonDocument.Parse(json);
        Assert.Equal($"Пользователь '{login}' мягко удалён.", doc.RootElement.GetProperty("message").GetString());
        Assert.Equal(adminUser.Login, targetUser.RevokedBy);
        Assert.NotNull(targetUser.RevokedOn);
    }

    // Успешно полностью удаляет пользователя и возвращает Ok
    [Fact]
    public void DeleteUser_ReturnsOk_WhenHardDeleteRequested()
    {
        var login = "toDelete";
        var targetUser = new User { Login = login, RevokedOn = null };
        var adminUser = new User { Login = "admin", Admin = true };

        _mockService.Setup(s => s.GetByLogin(login)).Returns(targetUser);
        _mockService.Setup(s => s.Remove(targetUser)).Verifiable();

        var controller = new TestableUsersController(_mockService.Object, adminUser);

        var result = controller.DeleteUser(login, new UserDeleteRequest { SoftDelete = false });

        var ok = Assert.IsType<OkObjectResult>(result);
        var json = JsonSerializer.Serialize(ok.Value);
        using var doc = JsonDocument.Parse(json);
        Assert.Equal($"Пользователь '{login}' полностью удалён.", doc.RootElement.GetProperty("message").GetString());

        _mockService.Verify(s => s.Remove(targetUser), Times.Once);
    }

    // Возвращает Unauthorized, если текущий пользователь не авторизован
    [Fact]
    public void RestoreUser_ReturnsUnauthorized_WhenAdminIsNull()
    {
        var controller = new TestableUsersController(_mockService.Object, null);

        var result = controller.RestoreUser("user1");

        var unauthorized = Assert.IsType<UnauthorizedObjectResult>(result);
        Assert.Equal("Вы не авторизованы.", unauthorized.Value);
    }

    // Возвращает BadRequest, если логин пустой
    [Fact]
    public void RestoreUser_ReturnsBadRequest_WhenLoginIsEmpty()
    {
        var controller = new TestableUsersController(_mockService.Object, new User { Admin = true });

        var result = controller.RestoreUser("   ");

        var badRequest = Assert.IsType<BadRequestObjectResult>(result);
        Assert.Equal("Логин пользователя не указан.", badRequest.Value);
    }

    // Возвращает NotFound, если пользователь не найден
    [Fact]
    public void RestoreUser_ReturnsNotFound_WhenUserDoesNotExist()
    {
        var login = "ghost";
        _mockService.Setup(s => s.GetByLogin(login)).Returns((User)null);
        var controller = new TestableUsersController(_mockService.Object, new User { Admin = true });

        var result = controller.RestoreUser(login);

        var notFound = Assert.IsType<NotFoundObjectResult>(result);
        Assert.Equal($"Пользователь с логином '{login}' не найден.", notFound.Value);
    }

    // Возвращает Conflict, если пользователь уже активен
    [Fact]
    public void RestoreUser_ReturnsConflict_WhenUserIsAlreadyActive()
    {
        var login = "activeUser";
        var user = new User { Login = login, RevokedOn = null };
        _mockService.Setup(s => s.GetByLogin(login)).Returns(user);
        var controller = new TestableUsersController(_mockService.Object, new User { Login = "admin", Admin = true });

        var result = controller.RestoreUser(login);

        var conflict = Assert.IsType<ConflictObjectResult>(result);
        Assert.Equal($"Пользователь '{login}' уже активен.", conflict.Value);
    }

    // Успешно восстанавливает пользователя и возвращает Ok
    [Fact]
    public void RestoreUser_ReturnsOk_WhenUserIsDeleted()
    {
        var login = "deletedUser";
        var user = new User
        {
            Login = login,
            RevokedOn = DateTime.UtcNow,
            RevokedBy = "oldAdmin"
        };

        var admin = new User { Login = "admin", Admin = true };
        _mockService.Setup(s => s.GetByLogin(login)).Returns(user);
        var controller = new TestableUsersController(_mockService.Object, admin);

        var result = controller.RestoreUser(login);

        var ok = Assert.IsType<OkObjectResult>(result);
        var json = JsonSerializer.Serialize(ok.Value);
        using var doc = JsonDocument.Parse(json);
        Assert.Equal($"Пользователь '{login}' успешно восстановлен.", doc.RootElement.GetProperty("message").GetString());

        Assert.Null(user.RevokedOn);
        Assert.Null(user.RevokedBy);
        Assert.Equal(admin.Login, user.ModifiedBy);
        Assert.NotNull(user.ModifiedOn);
    }



}


