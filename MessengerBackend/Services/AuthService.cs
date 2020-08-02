using System.Linq;
using System.Threading.Tasks;
using MessengerBackend.Database;
using MessengerBackend.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.Extensions.Configuration;

namespace MessengerBackend.Services
{
    public class AuthService
    {
        private readonly IConfiguration _configuration;
        private readonly MessengerDBContext _dbContext;

        public AuthService(MessengerDBContext dbContext, IConfiguration config)
        {
            _dbContext = dbContext;
            _configuration = config;
        }

        public Task<Session> GetSessionAsync(string token) =>
            _dbContext.Sessions.Where(s => s.RefreshToken == token).FirstOrDefaultAsync();

        public async Task<Session> GetAndDeleteSessionAsync(string token)
        {
            var session = await GetSessionAsync(token);
            if (session == null) return null;
            _dbContext.Sessions.Remove(session);
            await _dbContext.SaveChangesAsync();
            return session;
        }

        public async Task<EntityEntry<Session>> AddSessionAsync(Session session)
        {
            var s = await _dbContext.Sessions.AddAsync(session);
            await _dbContext.SaveChangesAsync();
            return s;
        }
    }
}