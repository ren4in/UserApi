using UserApi.Models;

namespace UserApi.Services;

public class UserService
{
    private readonly List<User> _users = new();

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


    public List<User> GetAll() => _users;
    public User? GetByLogin(string login) => _users.FirstOrDefault(u => u.Login == login);
    public void Add(User user) => _users.Add(user);
    public void Remove(User user)
    {
        _users.Remove(user);
    }


}
