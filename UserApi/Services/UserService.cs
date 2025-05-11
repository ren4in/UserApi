using UserApi.Models;
using UserApi.Services.UserApi.Services;

namespace UserApi.Services;

public class UserService : IUserService
{
    private readonly List<User> _users = new();
    public IEnumerable<User> GetAll()
    {
        return _users;
    }
    public UserService()
    {
        // Создание пользователя Admin
        _users.Add(new User
        {
            Login = "Admin",
            Password = "Admin123",
            Name = "Administrator",
            Gender = 2,
            Birthday = null,
            Admin = true,
            CreatedBy = "System"
        });
    }


     public User? GetByLogin(string login) => _users.FirstOrDefault(u => u.Login == login);
    public void Add(User user) => _users.Add(user);
    public void Remove(User user)
    {
        _users.Remove(user);
    }


}
