using LiteDB;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace Server
{
    class DbManager
    {
        const string DB_PATH = @"..\..\MyDB.db";
        const string DB_BACKUPS = "BackupRecord";
        const string DB_USERS = "UserRecord";
        public const string SESSIONID_NULL = "empty";


        /**
         *  Method that checks, for each record in the DB, if each file of the backup is actually present on the storage
         */
        public static void checkDBConsistency(string storagePath)
        {
            
            using (var db = new LiteDatabase(DB_PATH))
            {

                // Get a collection (or create, if doesn't exist)
                LiteCollection<BackupRecord> backupCollection = db.GetCollection<BackupRecord>(DB_BACKUPS);
                IEnumerable<BackupRecord> backupList = backupCollection.FindAll();

                // no backups on DB
                if (backupList.Count() <= 0)
                    return;

                foreach(BackupRecord record in backupList)
                {
                    List<myFileInfo> originalFileInfoList = record.fileInfoList;
                    List<myFileInfo> newFileInfoList = new List<myFileInfo>();
                    //string clientID = record.userID;

                    foreach (myFileInfo fi in originalFileInfoList) // foreach file contained in the record
                    {
                        // compute the local file name
                        string localFileName = fi.TimeStamp + Path.GetExtension(fi.Name);

                        //fix for null relative path (file in root directory)
                        string relativePath = fi.RelativePath;
                        if (relativePath == null)
                            relativePath = "";

                        string localFilePath = Path.Combine(storagePath, record.userID.TrimStart(Path.DirectorySeparatorChar), relativePath.TrimStart(Path.DirectorySeparatorChar), fi.Name.TrimStart(Path.DirectorySeparatorChar), localFileName.TrimStart(Path.DirectorySeparatorChar));
                        
                        // check if the file exists in the storage
                        if (File.Exists(localFilePath))
                        {
                            FileInfo f = new FileInfo(localFilePath);

                            if (fi.Size == f.Length)
                                newFileInfoList.Add(fi);
                            else
                                File.Delete(localFilePath);
                        }
                            
                    }

                    //if newFileInfoList is empty then delete backupRecord
                    if(newFileInfoList.Count <= 0)
                    {
                        Console.WriteLine("DB: deleted record " + record.userID + " " + record.timestamp);
                        backupCollection.Delete(record.Id);
                        continue;
                    }

                    //if newFileInfoList is different from originalFileInfoList then update the BackupRecord on DB
                    if(newFileInfoList.Count < originalFileInfoList.Count)
                    {
                        Console.WriteLine("DB: updated record " + record.userID + " " + record.timestamp);
                        record.fileInfoList = newFileInfoList;
                        backupCollection.Update(record.Id, record);
                    }
                }
                
            }

        }
        /**
         * Method that a new backup record in the database
         */
        public static bool insertBackupRecord(string client_id, string timestamp, string fileInfoJSONString)
        {   
            using (var db = new LiteDatabase(DB_PATH))
            {
                using (db.BeginTrans())
                {
                    // Get a collection (or create, if doesn't exist)
                    var col = db.GetCollection<BackupRecord>(DB_BACKUPS);

                    //insert backup record
                    List<myFileInfo> fileInfoList = JsonConvert.DeserializeObject<List<myFileInfo>>(fileInfoJSONString);
                    var backupRecord = new BackupRecord(client_id, timestamp, fileInfoList);
                    col.Insert(backupRecord);
                }
            }
            
            return true;
        }

        /**
         * Method that remove a backup record from the DB, if it exist
         */
        public static void removeBackupRecord(string clientID, string timestamp)
        {
            using (var db = new LiteDatabase(DB_PATH))
            {
                // Get a collection (or create, if doesn't exist)
                LiteCollection<BackupRecord> backupCollection = db.GetCollection<BackupRecord>(DB_BACKUPS);
                backupCollection.Delete(x => x.userID.Equals(clientID) && x.timestamp.Equals(timestamp));
                
            }
        }


        /* return all backup of user "clientID"
           if there are no backups, returns an empty list */
        public static List<BackupRecord> getBackupList(string clientID, string timestamp)
        {
            List<BackupRecord> backupList = new List<BackupRecord>();

            using (var db = new LiteDatabase(DB_PATH))
            {
                // Get a collection (or create, if doesn't exist)
                LiteCollection<BackupRecord> col = db.GetCollection<BackupRecord>(DB_BACKUPS);

                // Index document using document userID property
                col.EnsureIndex(x => x.userID);

                // find all backup records with userID = clientID
                IEnumerable<BackupRecord> results;

                //retreive all backups if timestamp is null
                if (timestamp == null)
                    results = col.Find(x => x.userID.Equals(clientID));
                else
                    results = col.Find(x => x.userID.Equals(clientID) && x.timestamp.Equals(timestamp));

                Console.WriteLine("totale record trovati " + results.LongCount<BackupRecord>());

                //if (results.LongCount<BackupRecord>() <= 0)
                    //throw new Exception("No backup found");

                //add backupRecord to list
                for (int i = 0; i < results.LongCount<BackupRecord>(); i++)
                {
                    //string recordString = JsonConvert.SerializeObject(results.ElementAt<BackupRecord>(i));
                    //BackupRecord record = JsonConvert.DeserializeObject<BackupRecord>(recordString);
                    BackupRecord record = results.ElementAt<BackupRecord>(i);
                    backupList.Add(record);
                    
                    //fix for relativePath= null (caused by SQL logic)
                    foreach(myFileInfo info in record.fileInfoList)
                    {
                        if (info.RelativePath == null)
                            info.RelativePath = "";
                    }

                    Console.WriteLine("record " + i + " : " + record.userID + " " + record.timestamp);
                }
            }

            return backupList;
        }


        /**
         * return:
            - "sessionID" of clientID if user already logged in
            - SESSIONID_NULL if user is not logged in
            - throw exception if some error occurs 
         */
        public static string getSessionID(string clientID)
        {
            string sessionID = null;

            if (clientID.Length <= 0)
                throw new Exception("Invalid clientID");

            using (var db = new LiteDatabase(DB_PATH))
            {
                // Get a collection (or create, if doesn't exist)
                LiteCollection<UserRecord> userCollection = db.GetCollection<UserRecord>(DB_USERS);

                // Index document using document userID property
                userCollection.EnsureIndex(x => x.UserID);

                //find record with selected userID(case insensitive)
                clientID = clientID.ToLower();
                IEnumerable<UserRecord> results = userCollection.Find(x => x.UserID.Equals(clientID));

                //clientID does not exist
                if (results.LongCount<UserRecord>() <= 0)
                    throw new Exception("ClientID does not exist");

                sessionID = results.ElementAt<UserRecord>(0).SessionID;

                //if (results.LongCount<UserRecord>() > 1)
                //    throw new IllegalDbStateException("Illegal state of DB! More users with same ID!");                
            }

            return sessionID;
        }

        /**
         * Method to check credentials, if valid return true and set sessionID on DB
         * return false if credentials are wrong
         * throw AlreadyLoggedInException if the client is already logged
         * throw UnexpectedLoginException if an expected exception is thrown
         */
        public static bool login(string clientID, string password, string sessionID) // throw AlreadyLoggedInException, UnexpectedLoginException
        {
            try
            {
                if (clientID.Length <= 0 || password.Length <= 0 || sessionID.Length <= 0)
                    throw new Exception("Invalid input parameters");

                using (var db = new LiteDatabase(DB_PATH))
                {
                    using (db.BeginTrans())
                    {
                        // Get a collection (or create, if doesn't exist)
                        LiteCollection<UserRecord> userCollection = db.GetCollection<UserRecord>(DB_USERS);

                        // Index document using document userID property
                        userCollection.EnsureIndex(x => x.UserID);
                        clientID = clientID.ToLower();
                        password = password.ToLower();
                        sessionID = sessionID.ToLower();

                        //find record with selected userID(case insensitive) and password(case insensitive, essendo lo sha1 della psw)
                        IEnumerable<UserRecord> results = userCollection.Find(x => x.UserID.Equals(clientID) && x.Password.Equals(password));

                        
                        if (results.LongCount<UserRecord>() <= 0)
                            // wrong credentials
                            return false;

                        // Check if the user is already logged
                        UserRecord loggedUser = results.ElementAt<UserRecord>(0);
                        //if actual SessionID on db is "empty" do login operation (setting new SessionID)
                        if (loggedUser.SessionID.Equals(SESSIONID_NULL))
                        {
                            UserRecord updatedUserRecord = new UserRecord(loggedUser.UserID, loggedUser.Password, sessionID);
                            userCollection.Update(loggedUser.Id, updatedUserRecord);
                            return true;
                        }
                        else
                            throw new AlreadyLoggedInException("Client '"+clientID+"' already logged in ");

                    }

                }
            }
            catch(AlreadyLoggedInException alie)
            {
                throw new AlreadyLoggedInException(alie.Message);
            }
            catch (Exception e)
            {
                Console.WriteLine(e.StackTrace);
                throw new UnexpectedLoginException("Unexpected exception in DBManage.Login(). Message:"+e.Message);
            }

        }


        /*
        method to perform logout operation
        
            */
        public static void Logout(string clientID)
        {
            if (clientID.Length <= 0)
                throw new Exception("[LOGOUT] invalid input parameters");

            using (var db = new LiteDatabase(DB_PATH))
            {
                using (db.BeginTrans())
                {
                    // Get a collection (or create, if doesn't exist)
                    LiteCollection<UserRecord> userCollection = db.GetCollection<UserRecord>(DB_USERS);

                    // Index document using document userID property
                    userCollection.EnsureIndex(x => x.UserID);

                    // Index document using document userID property
                    userCollection.EnsureIndex(x => x.UserID);
                    clientID = clientID.ToLower();

                    //find record with selected userID(case insensitive)
                    IEnumerable<UserRecord> results = userCollection.Find(x => x.UserID.Equals(clientID));

                    if (results.LongCount<UserRecord>() <= 0)
                        throw new Exception("[LOGOUT] clientID does not exist");
                    
                    //LOGOUT PROCEDURE: update user record (sessionID = "empty")
                    UserRecord loggedUser = results.ElementAt<UserRecord>(0);
                    
                    UserRecord updatedUserRecord = new UserRecord(loggedUser.UserID, loggedUser.Password, SESSIONID_NULL);

                    userCollection.Update(loggedUser.Id, updatedUserRecord);
                }

            }
            
        }

    }
}
