using UserApi.Models;
namespace UserApi.Services
{
  

    namespace UserApi.Services
    {
        public interface IUserService
        {
            User? GetByLogin(string login);
            IEnumerable<User> GetAll();
            void Add(User user);
            void Remove(User user);
        }
    }


}
