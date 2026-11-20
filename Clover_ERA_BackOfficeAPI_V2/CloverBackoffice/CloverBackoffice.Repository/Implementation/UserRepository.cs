using CloverBackoffice.Components;
using CloverBackoffice.Components.Enums;
using CloverBackoffice.Components.Helpers;
using CloverBackoffice.Infrastructure.DTO;
using CloverBackoffice.Infrastructure.Models;
using CloverBackoffice.Repository.Interfaces;
using CloverBackoffice.Repository.Provider;
using Microsoft.EntityFrameworkCore;
using System;
using System.Collections.Generic;
using System.Collections.Generic;
using System.ComponentModel.Design;
using System.Linq;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using static CloverBackoffice.Components.BussinessManager;

namespace CloverBackoffice.Repository.Implementation
{
    public class UserRepository : IUserRepository
    {
        private readonly ApplicationDbContext _context;

        public UserRepository(ApplicationDbContext context)
        {
            _context = context;
        }

        public async Task<UserDto> GetByUsername(string username, string token)
        {
            try
            {
                var createdUserDto = await _context.Users
                        .Where(u => u.Username == username)
                        .Select(u => new UserDto
                        {
                            UserId = u.UserId,
                            TenantId = u.TenantId,
                            Username = u.Username,
                            Name = u.Name,
                            IsActive = u.IsActive,
                            Group = new UserGroupDto
                            {
                                Id = u.UserGroup.Id,
                                Name = u.UserGroup.Name,
                                IsAdmin = u.UserGroup.IsAdmin,
                                UnitId = u.UserGroup.UnitId,
                                AssignUserRights = u.UserGroup.AssignUserRights.Select(right => new AssignUserRightDto
                                {
                                    AssignRightId = right.AssignRightId,
                                    ModuleID = right.SystemModule.ModuleID,
                                    ModuleName = right.SystemModule.ModuleName, // <-- Uses FK to join
                                    PromptTypeId = right.PromptType.PromptTypeId,
                                    PromptTypeTitle = right.PromptType.PromptTypeTitle  // <-- Uses FK to join
                                }).ToList()
                            }
                        })
                        .FirstOrDefaultAsync();
                if (!string.IsNullOrEmpty(token) && createdUserDto != null)
                {
                    // Save token in AuthTokens table
                    var authToken = new UserToken
                    {
                        UserId = createdUserDto.UserId,
                        Token = token,
                        ExpiryDate = DateTime.UtcNow.AddDays(3)
                    };
                    _context.UserTokens.Add(authToken);
                    await _context.SaveChangesAsync();
                }

                return createdUserDto;

            }
            catch (Exception ex)
            {

                throw;
            }
        }

        public async Task<List<UserStatusDto>> GetUserList(int profileId, int loggedInUserId)
        {
            // A RRIDA Admin is platform staff, not a member of the profile being
            // listed. Only another RRIDA Admin sees those rows; to everyone else the
            // profile's user list is just their own people.
            var loggedInUserGroupId = await _context.Users
                .Where(u => u.UserId == loggedInUserId)
                .Select(u => u.UserGroupId)
                .FirstOrDefaultAsync();

            bool isRridaAdmin = loggedInUserGroupId == (int)UserGroups.RridaAdmin;

            // firstTimeUserAdded — same rule as POST /api/company/upsert-user:
            // exactly 2 non-deleted users in the profile means the auto-created
            // Profile Admin plus the first user the admin added.
            //
            // Counted separately rather than from the list below so the two stay
            // independent of whatever the list itself is filtered by.
            var companyUserCount = await _context.Users
                .CountAsync(u => u.ProfileId == profileId && !u.IsDel);

            var firstTimeUserAdded = companyUserCount == 2;

            var users = await (from u in _context.Users
                          join g in _context.UserGroups
                              on u.UserGroupId equals g.Id
                          where u.ProfileId == profileId
                                && !u.IsDel
                                && (isRridaAdmin || u.UserGroupId != (int)UserGroups.RridaAdmin)
                          orderby u.Name
                          select new UserStatusDto
                          {
                              UserId = u.UserId,
                              TenantId = u.TenantId,
                              Username = u.Username,
                              Name = u.Name,
                              CreatedDate = u.CreatedDate.ToString(),
                              ModifiedDate = u.ModifiedDate.ToString(),
                              IsActive = u.IsActive,
                              UserGroupId = u.UserGroupId,
                              RoleName = g.Name,
                              LastLoginDate = u.LastLoginDate,
                              IsVerified = u.IsVerified,
                              Status = u.Status,
                              StatusName = GetStatusName(u.Status),
                              Type = u.Type,
                              IsFirstTimePasswordChange = u.IsFirstTimePasswordChange,
                              ProfileId = u.ProfileId,
                              FailedLoginAttempts = u.FailedLoginAttempts,
                              LastFailedLoginAt = u.LastFailedLoginAt
                          }).ToListAsync();

            // Per-company value, stamped on every row so the frontend can read it
            // from any element without changing the response shape.
            users.ForEach(u => u.FirstTimeUserAdded = firstTimeUserAdded);

            // Every role per user, one query for the whole list.
            var userIds = users.Select(u => u.UserId).ToList();
            var roleRows = await (from r in _context.UserRoles.AsNoTracking()
                                  join g in _context.UserGroups.AsNoTracking() on r.UserGroupId equals g.Id
                                  where userIds.Contains(r.UserId)
                                  select new { r.UserId, g.Id, g.Name })
                                 .ToListAsync();

            var rolesByUser = roleRows
                .GroupBy(r => r.UserId)
                .ToDictionary(g => g.Key, g => g
                    .OrderBy(x => RoleHierarchy.RankOf(x.Id))
                    .Select(x => new UserRoleDto { Id = x.Id, Name = x.Name })
                    .ToList());

            // No role rows: fall back to the derived column, unless it says "No Role"
            // (Profile Admin removed from a user who held nothing else) — then none.
            users.ForEach(u => u.Roles = rolesByUser.TryGetValue(u.UserId, out var held)
                ? held
                : u.UserGroupId == RoleHierarchy.NoRoleGroupId
                    ? new List<UserRoleDto>()
                    : new List<UserRoleDto> { new() { Id = u.UserGroupId, Name = u.RoleName } });

            ApplyRemovalRules(users, loggedInUserId);

            return users;
        }

        // Users.Status as the Suspend enum member is stored — a string column.
        private static readonly string SuspendedStatus = ((int)UserStatus.Suspend).ToString();

        /// <summary>
        /// Marks the rows whose Remove action the UI must not offer, so the screen
        /// and DELETE api/profile/user/{userId} say the same thing: a Profile Admin
        /// never removes themself, and the profile's last Profile Admin is never
        /// removable. The list already holds every non-deleted user of the profile,
        /// so the count is taken from it rather than from a second query.
        /// </summary>
        private static void ApplyRemovalRules(List<UserStatusDto> users, int loggedInUserId)
        {
            static bool IsProfileAdmin(UserStatusDto u) =>
                u.Roles.Any(r => r.Id == (int)UserGroups.ProfileAdmin);

            // A suspended admin cannot sign in, so they are not one of the admins
            // the profile is being kept above zero by. An invited one who has not
            // accepted yet is — they hold the role and can accept.
            var liveAdminIds = users
                .Where(u => IsProfileAdmin(u) && u.Status != SuspendedStatus)
                .Select(u => u.UserId)
                .ToHashSet();

            foreach (var user in users)
            {
                if (!IsProfileAdmin(user))
                    continue;

                if (user.UserId == loggedInUserId)
                {
                    user.CanRemove = false;
                    user.RemoveBlockedReason = ResponseMessages.Errors.ProfileAdminCannotRemoveSelf;
                    continue;
                }

                if (!liveAdminIds.Any(id => id != user.UserId))
                {
                    user.CanRemove = false;
                    user.RemoveBlockedReason = ResponseMessages.Errors.LastProfileAdminRequired;
                }
            }
        }

        public async Task<List<UserStatusDto>> GetTeamManagers(int profileId, int loggedInUserId)
        {
            var loggedInUserRole = await _context.Users
                .Where(u => u.UserId == loggedInUserId && !u.IsDel)
                .Select(u => u.UserGroupId)
                .FirstOrDefaultAsync();

            var query = from u in _context.Users
                        join g in _context.UserGroups on u.UserGroupId equals g.Id
                        where u.ProfileId == profileId
                              && !u.IsDel
                              && u.IsVerified
                              && u.Status == "1"
                        select new { u, g };

            //  Role-based condition
            if (loggedInUserRole == (int)UserGroups.RridaAdmin ||
                loggedInUserRole == (int)UserGroups.ProfileAdmin)
            {
                query = query.Where(x =>
                    x.u.UserGroupId == (int)UserGroups.TeamManager ||
                    x.u.UserGroupId == (int)UserGroups.ProfileAdmin);
            }
            else if (loggedInUserRole == (int)UserGroups.TeamManager)
            {
                query = query.Where(x =>
                    x.u.UserGroupId == (int)UserGroups.TeamManager);
            }

            return await query
                .OrderBy(x => x.u.Name)
                .Select(x => new UserStatusDto
                {
                    UserId = x.u.UserId,
                    TenantId = x.u.TenantId,
                    Username = x.u.Username,
                    Name = x.u.Name,
                    CreatedDate = x.u.CreatedDate.ToString(),
                    ModifiedDate = x.u.ModifiedDate.ToString(),
                    IsActive = x.u.IsActive,
                    UserGroupId = x.u.UserGroupId,
                    RoleName = x.g.Name,
                    LastLoginDate = x.u.LastLoginDate,
                    IsVerified = x.u.IsVerified,
                    Status = x.u.Status,
                    StatusName = GetStatusName(x.u.Status),
                    Type = x.u.Type,
                    IsFirstTimePasswordChange = x.u.IsFirstTimePasswordChange,
                    ProfileId = x.u.ProfileId
                })
                .ToListAsync();
        }


        public static string GetStatusName(string statusCode)
        {
            return statusCode switch
            {
                "1" => "Active",
                "2" => "Pending",
                "3" => "Suspended"
            };
        }

        public async Task<UserDto> GetUserInfoById(int UserId)
        {
            try
            {
                var createdUserDto = await _context.Users
                        .Where(u => u.UserId == UserId && !u.IsDel)
                        .Select(u => new UserDto
                        {
                            UserId = u.UserId,
                            TenantId = u.TenantId,
                            Username = u.Username,
                            Name = u.Name,
                            IsActive = u.IsActive,
                            Group = new UserGroupDto
                            {
                                Id = u.UserGroup.Id,
                                Name = u.UserGroup.Name,
                                IsAdmin = u.UserGroup.IsAdmin,
                                UnitId = u.UserGroup.UnitId,
                                AssignUserRights = u.UserGroup.AssignUserRights.Select(right => new AssignUserRightDto
                                {
                                    AssignRightId = right.AssignRightId,
                                    ModuleID = right.SystemModule.ModuleID,
                                    ModuleName = right.SystemModule.ModuleName, // <-- Uses FK to join
                                    PromptTypeId = right.PromptType.PromptTypeId,
                                    PromptTypeTitle = right.PromptType.PromptTypeTitle  // <-- Uses FK to join
                                }).ToList()
                            }
                        })
                        .FirstOrDefaultAsync();

                return createdUserDto;

            }
            catch (Exception ex)
            {

                throw;
            }
        }

        public async Task<UserDto> CreateUserAsync(string accessToken, User user)
        {
            try
            {
                user.IsEntraId = true;
                user.Password = PasswordUtility.Encrypt("12345678");
                user.Status = ((int)UserStatus.Active).ToString();
                user.Type = UserType.ProfileAdmin.ToString();
                user.CreatedDate = DateTime.UtcNow.ToString("o");
                user.ModifiedDate = DateTime.UtcNow.ToString("o");

                _context.Users.Add(user);
                await _context.SaveChangesAsync();

                if (!string.IsNullOrEmpty(accessToken))
                {
                    // Save token in AuthTokens table
                    var authToken = new UserToken
                    {
                        UserId = user.UserId,
                        Token = accessToken,
                        ExpiryDate = DateTime.UtcNow.AddDays(3)
                    };
                    _context.UserTokens.Add(authToken);
                    await _context.SaveChangesAsync();
                }

                var createdUserDto = await _context.Users
                        .Where(u => u.UserId == user.UserId)
                        .Select(u => new UserDto
                        {
                            UserId = u.UserId,
                            TenantId = u.TenantId,
                            Username = u.Username,
                            Name = u.Name,
                            IsActive = u.IsActive,
                            Group = new UserGroupDto
                            {
                                Id = u.UserGroup.Id,
                                Name = u.UserGroup.Name,
                                IsAdmin = u.UserGroup.IsAdmin,
                                UnitId = u.UserGroup.UnitId,
                                AssignUserRights = u.UserGroup.AssignUserRights.Select(right => new AssignUserRightDto
                                {
                                    AssignRightId = right.AssignRightId,
                                    ModuleID = right.SystemModule.ModuleID,
                                    ModuleName = right.SystemModule.ModuleName, // <-- Uses FK to join
                                    PromptTypeId = right.PromptType.PromptTypeId,
                                    PromptTypeTitle = right.PromptType.PromptTypeTitle            // <-- Uses FK to join
                                }).ToList()
                            }
                        })
                        .FirstOrDefaultAsync();

                return createdUserDto;

            }
            catch (Exception ex)
            {

                throw;
            }

        }
        public async Task<User> UpdateUserGroup(int UserId, int GroupId)
        {
            var user = await _context.Users.FindAsync(UserId);
            if (user == null)
            {
                return null;
            }
            user.UserGroupId = GroupId;
            await _context.SaveChangesAsync();
            return user;
        }
        public async Task<bool> UserExistsAsync(string email, string password)
        {
            return await _context.Users
                .AnyAsync(u => u.Username == email && u.Password == password);
        }
        public async Task<UserInfoResponseDto> GetUserInfoAsync()
        {
            var users = await _context.Users.ToListAsync();
            var companies = await _context.Profiles.ToListAsync();

            return new UserInfoResponseDto
            {
                Users = users,
                Profiles = companies
            };
        }
        public async Task<List<User>> GetAllUsersAsync()
        {
            return await _context.Users.ToListAsync();
        }
        public async Task<List<Profile>> GetAllCompaniesAsync()
        {
            return await _context.Profiles.ToListAsync();
        }
        public async Task<User> GetUserByEmailAsync(string email)
        {
            return await _context.Users
                .FirstOrDefaultAsync(u => u.Username == email);
        }

        public async Task<Profile> GetCompanyByUserEmailAsync(string email)
        {
            var user = await GetUserByEmailAsync(email);
            if (user == null)
                return null;

            return await _context.Profiles
                .FirstOrDefaultAsync(c => c.DomainName == email); // or match by ProfileId if you have that
        }
        public async Task<bool> UpdateUserAsync(User user)
        {
            var entry = _context.Users.Attach(user);

            // Handle CreatedDate
            if (!string.IsNullOrWhiteSpace(user.CreatedDate) &&
                DateTime.TryParse(user.CreatedDate, out DateTime parsedCreatedDate))
            {
                user.CreatedDate = DateTime.SpecifyKind(parsedCreatedDate, DateTimeKind.Utc).ToString("o");
                entry.Property(nameof(User.CreatedDate)).IsModified = true;
            }

            // Handle ModifiedDate
            if (!string.IsNullOrWhiteSpace(user.ModifiedDate) &&
                DateTime.TryParse(user.ModifiedDate, out DateTime parsedModifiedDate))
            {
                user.ModifiedDate = DateTime.SpecifyKind(parsedModifiedDate, DateTimeKind.Utc).ToString("o");
                entry.Property(nameof(User.ModifiedDate)).IsModified = true;
            }

            return await _context.SaveChangesAsync() > 0;

        }
        public async Task<User> GetUserByIdAsync(int userId)
        {
            return await _context.Users
                .FirstOrDefaultAsync(u => u.UserId == userId);
        }
        public async Task<List<NotificationDto>> GetNotificationUserByIdAsync(string token)
        {
            try
            {
                UserToken mdl = new UserToken();
                if (!string.IsNullOrEmpty(token))
                {
                    mdl = await _context.UserTokens
                   .OrderByDescending(a => a.AuthId)
                   .LastOrDefaultAsync(a => a.Token == token);
                }
                if (mdl == null) return null;

                var userInfo = await _context.Users.FirstOrDefaultAsync(u => u.UserId == mdl.UserId);
                if (userInfo == null) return null;

                List<Notification> notifications;
                if (userInfo.UserGroupId == (int)UserGroups.RridaAdmin)
                {
                    notifications = await _context.Notifications
                        .OrderByDescending(n => n.CreatedAt)
                        .ToListAsync();
                }
                else if (userInfo.UserGroupId == (int)UserGroups.ProfileAdmin)
                {
                    notifications = await _context.Notifications
                        .Where(n => n.UserId == userInfo.UserId)
                        .OrderByDescending(n => n.CreatedAt)
                        .ToListAsync();
                }
                else
                {
                    notifications = new List<Notification>();
                }

                // Fetch related data in bulk to avoid per-notification queries
                var userIds = notifications.Select(n => n.UserId).Distinct().ToList();
                var profileIds = notifications.Select(n => n.ProfileId).Distinct().ToList();
                var unitIds = notifications.Select(n => n.BussinessUnitId).Distinct().ToList();

                var allUsers = await _context.Users
                    .Where(u => userIds.Contains(u.UserId))
                    .ToDictionaryAsync(u => u.UserId);

                var allUserGroups = await _context.UserGroups
                    .ToDictionaryAsync(ug => ug.Id);

                var allCompanies = await _context.Profiles
                    .Where(c => profileIds.Contains((int)c.Id))
                    .ToDictionaryAsync(c => c.Id);

                //var allUnits = await _context.CompanyUnits
                //    .Where(u => unitIds.Contains(u.UnitId))
                //    .ToDictionaryAsync(u => u.UnitId);


                var allUnits = await _context.BusinessUnits
                .Where(u => unitIds.Contains((int)u.Id))
                .ToDictionaryAsync(u => u.Id);


                var dtoList = notifications.Select(notification =>
                {
                    allUsers.TryGetValue(notification.UserId, out var notifUser);
                    allUserGroups.TryGetValue(notifUser?.UserGroupId ?? 0, out var notifUserGroup);
                    allCompanies.TryGetValue(Convert.ToUInt32(notification.ProfileId), out var company);
                    allUnits.TryGetValue((uint)notification.BussinessUnitId, out var unit);

                    return new NotificationDto
                    {
                        UserName = notifUser?.Name,
                        CompanyName = company?.ProfileName,
                        UnitName = unit?.Name,
                        Type = notifUserGroup?.Name,
                        CreatedAt = notification.CreatedAt,
                        Message = notification.Message
                    };
                }).ToList();

                return dtoList;
            }
            catch (Exception ex)
            {
                throw;
            }
        }

        public async Task<bool> InvalidateUserTokensAsync(int userId)
        {
            try
            {
                // Get all tokens for the user
                var userTokens = await _context.UserTokens
                    .Where(ut => ut.UserId == userId)
                    .ToListAsync();

                if (userTokens.Any())
                {
                    // Remove all tokens for this user
                    _context.UserTokens.RemoveRange(userTokens);
                    await _context.SaveChangesAsync();
                }

                return true;
            }
            catch (Exception ex)
            {
                throw;
            }
        }
    }
}


