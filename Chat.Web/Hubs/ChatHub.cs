﻿using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.AspNet.SignalR;
using Chat.Web.Models.ViewModels;
using Chat.Web.Models;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using AutoMapper;
using Microsoft.AspNet.Identity;
using Microsoft.AspNet.Identity.EntityFramework;

namespace Chat.Web.Hubs
{
    [Authorize]
    public class ChatHub : Hub
    {
       

        #region Properties
        /// <summary>
        /// List of online users
        /// </summary>
        public readonly static List<UserViewModel> _Connections = new List<UserViewModel>();

        /// <summary>
        /// List of available chat rooms
        /// </summary>
        private readonly static List<RoomViewModel> _Rooms = new List<RoomViewModel>();

        /// <summary>
        /// Mapping SignalR connections to application users.
        /// (We don't want to share connectionId)
        /// </summary>
        private readonly static Dictionary<string, string> _ConnectionsMap = new Dictionary<string, string>();
        #endregion

        public void Send(string roomName, string message)
        {
           SendToRoom(roomName, message);
        }
         
        public void SendToRoom(string roomName, string message)
        {
            try
            {
                using (var db = new ApplicationDbContext())
                {
                    var user = db.Users.Where(u => u.UserName == IdentityName).FirstOrDefault();
                    var room = db.Rooms.Where(r => r.Name == roomName).FirstOrDefault();

                    var userManager = new ApplicationUserManager(new UserStore<ApplicationUser>(new ApplicationDbContext()));
                    bool isAdminUser = userManager.IsInRole(user.Id, Chat.Web.Models.Roles.AdminRoleName);

                    // Create and save message in database
                    Message msg = new Message()
                    {
                        Content = Regex.Replace(message, @"(?i)<(?!img|a|/a|/img).*?>", String.Empty),
                        Timestamp = DateTime.Now.Ticks.ToString(),
                        FromUser = user,
                        ToRoom = room,
                        IsAdmin = isAdminUser 
                    };

                        db.Messages.Add(msg);
                        db.SaveChanges();

                        // Broadcast the message
                        var messageViewModel = Mapper.Map<Message, MessageViewModel>(msg);
                        Clients.Group(roomName).newMessage(messageViewModel);
                    
                }
            }
            catch (Exception)
            {
                Clients.Caller.onError("Message not send!");
            }
        }

        public void Join(string roomName)
        {
            try
            {
                var user = _Connections.Where(u => u.Username == IdentityName).FirstOrDefault();
                if (user.CurrentRoom != roomName)
                {
                    // Remove user from others list
                    if (!string.IsNullOrEmpty(user.CurrentRoom))
                        Clients.OthersInGroup(user.CurrentRoom).removeUser(user);

                    // Join to new chat room
                    Leave(user.CurrentRoom);
                    Groups.Add(Context.ConnectionId, roomName);
                    user.CurrentRoom = roomName;
                    SendToRoom( roomName , "Joined");


                    // Tell others to update their list of users
                    Clients.OthersInGroup(roomName).addUser(user);
                }
            }
            catch (Exception ex)
            {
                Clients.Caller.onError("You failed to join the chat room!" + ex.Message);
            }
        }

        private void Leave(string roomName)
        {
            Groups.Remove(Context.ConnectionId, roomName);
            SendToRoom(roomName, "Left");
        }

        public void CreateRoom(string roomName)
        {
            try
            {
                using (var db = new ApplicationDbContext())
                {
                    // Accept: Letters, numbers and one space between words.
                    Match match = Regex.Match(roomName, @"^\w+( \w+)*$");
                    if (!match.Success)
                    {
                        Clients.Caller.onError("Invalid room name!\nRoom name must contain only letters and numbers.");
                    }
                    else if (roomName.Length < 5 || roomName.Length > 20)
                    {
                        Clients.Caller.onError("Room name must be between 5-20 characters!");
                    }
                    else if (db.Rooms.Any(r => r.Name == roomName))
                    {
                        Clients.Caller.onError("Another chat room with this name exists");
                    }
                    else
                    {
                        // Create and save chat room in database
                        var user = db.Users.Where(u => u.UserName == IdentityName).FirstOrDefault();
                        var room = new Room()
                        {
                            Name = roomName,
                            UserAccount = user
                        };
                        db.Rooms.Add(room);
                        db.SaveChanges();

                        if (room != null)
                        {
                            // Update room list
                            var roomViewModel = Mapper.Map<Room, RoomViewModel>(room);
                            _Rooms.Add(roomViewModel);
                            Clients.All.addChatRoom(roomViewModel);
                        }
                    }
                }//using
            }
            catch (Exception ex)
            {
                Clients.Caller.onError("Couldn't create chat room: " + ex.Message);
            }
        }

        

       /* public IEnumerable<MessageViewModel> GetMessageHistory(string roomName)
        {
            using (var db = new ApplicationDbContext())
            {
                var messageHistory = db.Messages.Where(m => m.ToRoom.Name == roomName)
                    .OrderByDescending(m => m.Timestamp)
                    .Take(20)
                    .AsEnumerable()
                    .Reverse()
                    .ToList();

                return Mapper.Map<IEnumerable<Message>, IEnumerable<MessageViewModel>>(messageHistory);
            }
        }*/

        public IEnumerable<RoomViewModel> GetRooms()
        {
            using (var db = new ApplicationDbContext())
            {
                // First run?
                if (_Rooms.Count == 0)
                {
                    foreach (var room in db.Rooms)
                    {
                        var roomViewModel = Mapper.Map<Room, RoomViewModel>(room);
                        _Rooms.Add(roomViewModel);
                    }
                }
            }

            return _Rooms.ToList();
        }

        public IEnumerable<UserViewModel> GetUsers(string roomName)
        {
            return _Connections.Where(u => u.CurrentRoom == roomName).ToList();
        }

        #region OnConnected/OnDisconnected
        public override Task OnConnected()
        {
            using (var db = new ApplicationDbContext())
            {
                try
                {
                    var user = db.Users.Where(u => u.UserName == IdentityName).FirstOrDefault();

                    var userViewModel = Mapper.Map<ApplicationUser, UserViewModel>(user);
                 
                    userViewModel.CurrentRoom = "";

                    _Connections.Add(userViewModel);
                    _ConnectionsMap.Add(IdentityName, Context.ConnectionId);

                    Clients.Caller.getProfileInfo(user.DisplayName, user.Avatar);
                }
                catch (Exception ex)
                {
                    Clients.Caller.onError("OnConnected:" + ex.Message);
                }
            }

            return base.OnConnected();
        }

        public override Task OnDisconnected(bool stopCalled)
        {
            try
            {
                var user = _Connections.Where(u => u.Username == IdentityName).First();
                _Connections.Remove(user);

                // Tell other users to remove you from their list
                Clients.OthersInGroup(user.CurrentRoom).removeUser(user);

                // Remove mapping
                _ConnectionsMap.Remove(user.Username);
            }
            catch (Exception ex)
            {
                Clients.Caller.onError("OnDisconnected: " + ex.Message);
            }

            return base.OnDisconnected(stopCalled);
        }

        public override Task OnReconnected()
        {
            var user = _Connections.Where(u => u.Username == IdentityName).First();
            Clients.Caller.getProfileInfo(user.DisplayName, user.Avatar);

            return base.OnReconnected();
        }
        #endregion

        private string IdentityName
        {
            get { return Context.User.Identity.Name; }
        }

    }
}