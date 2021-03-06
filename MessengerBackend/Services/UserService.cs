﻿using System.Threading.Tasks;
using MessengerBackend.Database;
using MessengerBackend.Models;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace MessengerBackend.Services
{
    public class UserService
    {
        private readonly MessengerDBContext _dbContext;

        public UserService(MessengerDBContext dbContext) => _dbContext = dbContext;
        public DbSet<User> Users => _dbContext.Users;

        public async Task<User?> AddUserAsync(string number, string firstName, string lastName, string username)
        {
            var newUser = new User
            {
                Number = number,
                FirstName = firstName,
                LastName = lastName,
                Username = username
            };
            try
            {
                var user = await _dbContext.Users.AddAsync(newUser);
                await _dbContext.SaveChangesAsync();
                return user.Entity;
            }
            catch (DbUpdateException e)
                when ((e.InnerException as PostgresException)?.SqlState == PostgresErrorCodes.UniqueViolation)
            {
                return null;
            }
        }

        public async Task<bool> SaveUserAsync(User user)
        {
            try
            {
                _dbContext.Users.Attach(user);
                await _dbContext.SaveChangesAsync();
                return true;
            }
            catch (DbUpdateException e)
                when ((e.InnerException as PostgresException)?.SqlState.EqualsAnyString(
                    PostgresErrorCodes.UniqueViolation,
                    PostgresErrorCodes.StringDataRightTruncation) ?? false)
            {
                return false;
            }
        }
    }
}