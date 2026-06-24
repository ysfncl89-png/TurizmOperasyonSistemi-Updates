using TurizmOperasyonSistemi.Models;

namespace TurizmOperasyonSistemi.Services
{
    public class CurrentUser
    {
        private static CurrentUser? _instance;
        private User? _user;

        public User? User => _user;

        private CurrentUser()
        {
        }

        public static CurrentUser Instance
        {
            get
            {
                if (_instance == null)
                {
                    _instance = new CurrentUser();
                }
                return _instance;
            }
        }

        public void SetUser(User user)
        {
            _user = user;
        }

        public void Clear()
        {
            _user = null;
        }

        public bool IsLoggedIn => _user != null;
    }
}
