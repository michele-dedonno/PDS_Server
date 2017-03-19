using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Server
{
    /*
    class rappresenting the user record on liteDB
    */
    public class UserRecord
    {
        public int Id { get; set; }

        public string UserID { get; set; }
        public string Password { get; set; }
        public string SessionID { get; set; }

        public UserRecord() { }

        public UserRecord(string id, string password, string sessionID)
        {
            UserID = id;
            Password = password;
            SessionID = sessionID;
        }
    }
}
