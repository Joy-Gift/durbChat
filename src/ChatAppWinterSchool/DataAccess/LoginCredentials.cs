﻿namespace ChatAppWinterSchool
{
    public class LoginCredentials
    {
        public string NickName { get; set; }
        public string Password { get; set; }
        
        public LoginCredentials(string Nickname , string Password)
        {
            this.Password = Password;
            this.NickName = NickName;
        }

    }


}





