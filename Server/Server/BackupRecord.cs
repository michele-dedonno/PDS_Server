using System;
using System.Collections.Generic;
using System.Globalization;

namespace Server
{
    public class BackupRecord
    {
        public int Id { get; set; }

        public string userID { get; set; }
        public string timestamp { get; set; }
        public List<myFileInfo> fileInfoList { get; set; }



        public BackupRecord(string userID, string timestamp, List<myFileInfo> list)
        {
            this.userID = userID;
            this.timestamp = timestamp;
            this.fileInfoList = list;
        }

        //for test
        public BackupRecord(List<myFileInfo> list)
        {
            this.userID = null;
            this.timestamp = null;
            this.fileInfoList = list;
        }

        public BackupRecord() { }

        

        public DateTime getDateTime()
        {
            DateTime dt = DateTime.ParseExact(timestamp, "yyyyMMddHHmmssfff", CultureInfo.InvariantCulture);

            return dt;
        }

        public string getDateString()
        {
            DateTime d = getDateTime();
            return "Backup " + d.Day + "/" + d.Month + "/" + d.Year;
        }

        public string getTimeString()
        {
            DateTime d = getDateTime();
            return d.Hour + ":" + d.Minute + ":" + d.Second;
        }
    }
}
