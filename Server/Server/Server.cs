using System;
using System.Collections;
using System.Net;
using System.Net.Sockets;
using System.Net.Security;
using System.Security.Authentication;
using System.Text;
using System.Security.Cryptography.X509Certificates;
using System.IO;
using System.Security.Cryptography;
using System.Linq;
using System.Threading;
using System.Collections.Generic;
using Newtonsoft.Json;
using System.Globalization;

namespace Server
{
     public class Server
    {
        const int MAX_THREADS_NUMBER = 10;
        const int PORT = 444;
        const int BUFFER_DIM = 10000000;
        const string POST_BACKUP = "POST_BACKUP"; // Message sent by client to server when start a new backup phase
        const string GET_BACKUP = "GET_BACKUP"; // Message sent by client to server when requires to restore a backup
        const string LOGIN = "LOGIN"; // Message sent by client to server when requires to log in
        const string LOGIN_OK = "LOGIN_OK"; // Message sent by server to client when login credential are right
        const string LOGIN_CREDENTIALS_ERR = "LOGIN_CREDENTIALS_ERR"; // Message sent by server to client when login credential are wrong
        const string LOGIN_GENERIC_ERR = "LOGIN_GENERIC_ERROR"; // Message sent by server to client when there is a generic error in the login procedure
        const string LOGIN_ALREADY_LOGGED = "LOGIN_ALREADY_LOGGED";// Message sent by server to client when that user is already logged
        const string AUTHENTICATION_OK = "AUTHENTICATION_OK"; // Message sent by the server to the client when the session authentication has completed successfully
        const string AUTHENTICATION_ERR = "AUTHENTICATION_ERR"; // Message sent by the server to the client when the session authentication has not completed successfully
        const string LOGOUT = "LOGOUT"; // Message sent by client to server when requires to log out
        const string LOGOUT_OK = "LOGOUT_OK"; // Message sent by server to client when log out successfully
        const string LOGOUT_ERR = "LOGOUT_ERR"; // Message sent by server to client when log out fails

        const string LIST_BACKUPS = "LIST_BACKUPS"; // Message sent by client to server when requires the list of backups related to a clientID
        const string END_BACKUP = "END_BACKUP"; // Message sent by server to client when the backup is finished (in download or upload)
        const string FILE_LIST = "FILE_LIST"; // Message sent by server to client when there are some file to backup. It is followed by the file list.
        const string END_REQUEST = "END_REQUEST"; // Message sent by client to server when the interaction is finished and the connection can be closed

        static string Storage_Path = null;
        static X509Certificate serverCertificate = null;
        private static long numberConnection = 0;

        // The certificate parameter specifies the name of the file containing the machine certificate.
        public static void RunServer(string certificate, string psw)
        {
            myTcpListener listener = null;
            try
            {
                // Inizialize path of the server storage
                Server.Storage_Path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "ServerStorage");
                Console.WriteLine("Server avviato. Storage Directory: " + Server.Storage_Path);

                // check DB consistency
                Console.WriteLine("Verifica della consistenza del DB in corso...");
                DbManager.checkDBConsistency(Server.Storage_Path);
                Console.WriteLine("DB reso consistente con lo storage.");

                //serverCertificate = X509Certificate2.CreateFromCertFile(certificate);
                serverCertificate = new X509Certificate2(certificate, psw);
                // Create a TCP/IP (IPv4) socket and listen for incoming connections.
                listener = new myTcpListener(IPAddress.Any, PORT);
                listener.Start();

            }
            catch (Exception e)
            {
                Console.WriteLine("Errore nell'avvio del server: " + e.Message);
                Console.WriteLine(e.StackTrace);
                return;
            }

            // Setto il numero massimo di client che possono essere serviti contemporaneamente
            ThreadPool.SetMaxThreads(MAX_THREADS_NUMBER, MAX_THREADS_NUMBER);

            Console.WriteLine("Waiting for a client to connect...");
            // Server Main Loop
            while (true)
            {
                try
                {
                    if (listener != null && listener.Active)
                    {
                        // Application blocks while waiting for an incoming connection. Type CNTL-C to terminate the server.
                        TcpClient client = listener.AcceptTcpClient();
                        ThreadPool.QueueUserWorkItem(ProcessClient, client); //usage: ProcessClient(client);
                    }
                    else
                        break; // il listener non è più funzionante, chiudo il server
                }
                catch (SocketException se)
                {
                    Console.WriteLine("Socket Exception: '" + se.Message + "'. Error code: " + se.ErrorCode + "'");
                    Console.WriteLine(se.StackTrace);
                    continue;  // servi il prossimo client                    
                }
                catch (Exception e)
                {
                    Console.WriteLine("Errore durante il processing del client: '" + e.Message + "'");
                    Console.WriteLine(e.StackTrace);
                    continue; // servi il prossimo client
                }

            }

        }

        private static void ProcessClient(object clientObject)
        {
            
            SslStream sslStream = null;
            TcpClient client = (TcpClient)clientObject;

            Interlocked.Increment(ref numberConnection); // increment numberConnection atomically
            long nConnection = Interlocked.Read(ref numberConnection); // read numberConnection atomically

            Console.WriteLine("===============================================================================================");
            Console.WriteLine("Inizio processing di un nuovo client. Numero di connessioni attive: " + nConnection.ToString());
            if (client == null)
                throw new Exception("Invialid TcpClient object");

            try
            {
                // A client has connected. Create the SslStream using the client's network stream.
                sslStream = new SslStream(client.GetStream(), false);

                // Authenticate the server but don't require the client to authenticate.
                sslStream.AuthenticateAsServer(serverCertificate, false, SslProtocols.Tls, true);

                // Display the properties and settings for the authenticated stream.
                //DisplaySecurityLevel(sslStream);
                //DisplaySecurityServices(sslStream);
                //DisplayCertificateInformation(sslStream);
                //DisplayStreamProperties(sslStream);

                // Set timeouts for the read and write to 10 seconds.
                sslStream.ReadTimeout = 10000;
                sslStream.WriteTimeout = 10000;

                bool loop = true;
                do
                {
                    // Receive Request?0
                    string firstMessageRcv = ReadMessage(sslStream, "\r\n"); // "AZIONE?0"
                    Console.WriteLine(">Client: " + firstMessageRcv);
                    string[] stringArray = firstMessageRcv.Split(new Char[] { '?' });
                    if (stringArray.Length != 2)
                        //Console.WriteLine("Messaggio ricevuto dal client mal formattato");
                        throw new Exception("File name message malformed: " + firstMessageRcv);

                    //if (!isRightChallenge(challenge, stringArray[1], "0"))
                    if(!stringArray[1].Equals("0"))
                        throw new Exception("Messaggio 0 sbagliato: " + stringArray[1]);

                    /* Process client request */
                    switch (stringArray[0])
                    {
                        case LIST_BACKUPS: // Client ask the list of available backups
                            processGetBackupList(sslStream);
                            break;

                        case POST_BACKUP: // Client create a new backup       
                            processPostBackup(sslStream);
                            break;

                        case GET_BACKUP: // Client retrieve an older backup
                            processGetBackup(sslStream);
                            break;

                        case LOGIN:
                            processLogin(sslStream);
                            break;

                        case LOGOUT:
                            processLogout(sslStream);
                            break;

                        case END_REQUEST: // Client request has been successfully processed
                            loop = false;
                            Console.WriteLine(">Server - Richiesta del client processata correttamente");
                            break;
                        default:
                            // client thread ends
                            break;
                    }
                } while (loop);
            
            }
            catch (AuthenticationException e)
            {
                Console.WriteLine("Errore di autenticazione: ", e.Message);
                if (e.InnerException != null)
                {
                    Console.WriteLine("\tInner exception: ", e.InnerException.Message);
                }
                Console.WriteLine("Authentication failed.");
            }
            catch (FormatException fe)
            {
                Console.WriteLine(fe.StackTrace);
                Console.WriteLine("Format Exception: " + fe.Message);
            }
            catch (IOException ioe)
            {
                Console.WriteLine("Errore nella comunicazione con il client: " + ioe.Message);
                Console.WriteLine(ioe.StackTrace);
            }
            catch (Exception e)
            {
                Console.WriteLine("Errore durante il processing del client: '" + e.Message + "'");
                Console.WriteLine(e.StackTrace);
            }
            finally
            {
                // The client stream will be closed with the sslStream because we specified this behavior when creating the sslStream.
                Console.WriteLine("Closing the connection...");
                if (sslStream != null)
                    sslStream.Close();
                if (client != null)
                    client.Close();

                Interlocked.Decrement(ref numberConnection); // decrement numberConnection atomically
                nConnection = Interlocked.Read(ref numberConnection); // read numberConnection atomically
                Console.WriteLine("Thread chiuso. Numero di connessioni attive: " + nConnection.ToString());
                //Console.WriteLine("Connection closed");
            }
        }

        /**
         * Method that processes the client request of logging in
         */
        private static void processLogin(SslStream sslStream)
        {


            /* Send ACK:0 */
            sslStream.Write(Encoding.UTF8.GetBytes("ACK:0\r\n"));
            sslStream.Flush();
            Console.WriteLine(">Server: ACK:0");


            /* Receive ClientID and sha1 password */
            string messageRcv = ReadMessage(sslStream, "\r\n"); // "ClientID?sha1Psw"
            Console.WriteLine(">Client: " + messageRcv);
            string[] stringArray = messageRcv.Split(new Char[] { '?' });
            if (stringArray.Length != 2)
                throw new Exception("Credentials message malformed: " + messageRcv);

            string clientID = stringArray[0].ToLower();
            string sha1Password = stringArray[1].ToLower();

            // Compute session ID
            string sessionID = CreateSessionID(clientID, sha1Password);
            if (sessionID == null)
                throw new Exception("Il metodo CreateSectionID() ha restituito null");
            // Compute sha1 sessionID
            string sha1SessionID = computeSHA1String(sessionID);
            if (sha1SessionID == null)
                throw new Exception("Il metodo computeSHA1String(sessionID) ha restituito null");
            sha1SessionID = sha1SessionID.ToLower();

            try
            {
                if (!DbManager.login(clientID, sha1Password, sha1SessionID)) // save sha1 session ID in DB
                {
                    //Console.WriteLine("Il metodo DbManager.Login() ha ritornato false: credenziali di login sbagliate");

                    /* send LOGIN_ERR to the client */
                    string errorMessage = LOGIN_CREDENTIALS_ERR;
                    Console.WriteLine(">Server: " + errorMessage);
                    errorMessage += "\r\n";
                    sslStream.Write(Encoding.UTF8.GetBytes(errorMessage));
                    sslStream.Flush();
                }
                else
                {
                    //Console.WriteLine("Il metodo DbManager.Login() ha ritornato true: login avvenuto con successo");
                    /* send LOGIN_OK to the client */
                    string okMessage = LOGIN_OK;
                    Console.WriteLine(">Server: " + okMessage);
                    okMessage += "\r\n";
                    sslStream.Write(Encoding.UTF8.GetBytes(okMessage));
                    sslStream.Flush();

                    /* Receive ACK:1 */
                    string responseMessage = ReadMessage(sslStream, "\r\n");
                    Console.WriteLine(">Client: " + responseMessage);
                    if (!responseMessage.Equals("ACK:1"))
                        throw new Exception("Ricezione ACK 1 fallita");

                    /* Send Session ID */
                    sslStream.Write(Encoding.UTF8.GetBytes(sessionID+"\r\n"));
                    sslStream.Flush();
                    Console.WriteLine(">Server (sessionID): " + sessionID);
                }
            }
            catch (AlreadyLoggedInException ale)
            {
                //Console.WriteLine("Il metodo DBManager.Login() ha lanciato un'eccezione di tipo AlreadyLoggedInException");

                /* send LOGIN_ALREADY_LOGGED to the client */
                string errorMessage = LOGIN_ALREADY_LOGGED;
                Console.WriteLine(">Server: " + errorMessage);
                errorMessage += "\r\n";
                sslStream.Write(Encoding.UTF8.GetBytes(errorMessage));
                sslStream.Flush();
            }
            catch (UnexpectedLoginException e)
            {
                //Console.WriteLine("Il metodo DBManager.Login() ha lanciato un'eccezione inaspettata");

                /* send LOGIN_GENERIC_ERR to the client */
                string errorMessage = LOGIN_GENERIC_ERR;
                Console.WriteLine(">Server: " + errorMessage);
                errorMessage += "\r\n";
                sslStream.Write(Encoding.UTF8.GetBytes(errorMessage));
                sslStream.Flush();
            }

        }

        /**
         *  Method that processes the client request of logging out
         */
        private static void processLogout(SslStream sslStream)
        {
            /* Send ACK:0 */
            sslStream.Write(Encoding.UTF8.GetBytes("ACK:0\r\n"));
            sslStream.Flush();
            Console.WriteLine(">Server: ACK:0");

            /* Receive ClientID */
            string messageRcv = ReadMessage(sslStream, "\r\n"); // "ClientID"
            Console.WriteLine(">Client (clientID): " + messageRcv);

            string clientID = messageRcv.ToLower();

            try
            {
                // perform log out operation
                DbManager.Logout(clientID);

                /* send LOGOUT_OK to the client */
                string okMessage = LOGOUT_OK;
                Console.WriteLine(">Server: " + okMessage);
                okMessage += "\r\n";
                sslStream.Write(Encoding.UTF8.GetBytes(okMessage));
                sslStream.Flush();

            }
            catch(Exception e)
            {
                /* send LOGOUT_ERR to the client */
                string okMessage = LOGOUT_ERR;
                Console.WriteLine(">Server: " + okMessage);
                okMessage += "\r\n";
                sslStream.Write(Encoding.UTF8.GetBytes(okMessage));
                sslStream.Flush();
            }
        }


        /**
         * Method that processes the client request of backup list
         */
        private static void processGetBackupList(SslStream sslStream)
        {
            List<BackupRecord> backupList = null;
            string clientID = null;
            string sessionID = null;

            /* Send ACK:0 */
            sslStream.Write(Encoding.UTF8.GetBytes("ACK:0\r\n"));
            sslStream.Flush();
            Console.WriteLine(">Server: ACK:0");

            /* Receive ClientID AND Session ID */
            string messageRcv = ReadMessage(sslStream, "\r\n"); // "ClientID?SessionID"
            Console.WriteLine(">Client: " + messageRcv);
            string[] stringArray = messageRcv.Split(new Char[] { '?' });
            if (stringArray.Length != 2)
                throw new Exception("Authentication message malformed: " + messageRcv);

            clientID = stringArray[0].ToLower();
            sessionID = stringArray[1].ToLower();

            // Compute sha1 sessionID
            string sha1SessionID = computeSHA1String(sessionID);
            if (sha1SessionID == null)
                throw new Exception("Il metodo computeSHA1String(sessionID) ha restituito null");
            sha1SessionID = sha1SessionID.ToLower();

            /* Check session ID */
            string dbSessionID = DbManager.getSessionID(clientID);
            string responseMessage = null;
            if (dbSessionID.Equals(sha1SessionID))
            {
                /* Send AUTHENTICATION_OK to the client */
                responseMessage = AUTHENTICATION_OK;
                Console.WriteLine(">Server: " + responseMessage);
                responseMessage += "\r\n";
                sslStream.Write(Encoding.UTF8.GetBytes(responseMessage));
                sslStream.Flush();
            }
            else
            {
                /* Send AUTHENTICATION_ERR to the client */
                responseMessage = AUTHENTICATION_ERR;
                Console.WriteLine(">Server: " + responseMessage);
                responseMessage += "\r\n";
                sslStream.Write(Encoding.UTF8.GetBytes(responseMessage));
                sslStream.Flush();
                return;
            }


            /* Receive timestamp AND sessionID:1*/
            messageRcv = ReadMessage(sslStream, "\r\n"); // "timestamp?sessionID:1"
            Console.WriteLine(">Client (timestamp): " + messageRcv);
            stringArray = messageRcv.Split(new Char[] { '?' });
            if (stringArray.Length != 2)
                throw new Exception("Timestamp message malformed: " + messageRcv);

            if (!isRightChallenge(sessionID, stringArray[1], "1"))
                throw new Exception("Challenge 1 sbagliato: " + stringArray[1]);

            string timestamp = stringArray[0];
            if (timestamp.Equals("NULL"))
                backupList = DbManager.getBackupList(clientID, null);
            else
                backupList = DbManager.getBackupList(clientID, timestamp);

            // Se la lista è vuota, mando una lista vuota al client

            /* send json backupList to the client */
            string jsonList = JsonConvert.SerializeObject(backupList);
            sslStream.Write(Encoding.UTF8.GetBytes(jsonList + "\r\n"));
            sslStream.Flush();
            //Console.WriteLine(">Server (backup list): " + jsonList);

            /* Receive ACK:1 */
            responseMessage = ReadMessage(sslStream, "\r\n");
            Console.WriteLine(">Client: " + responseMessage);
            if (!responseMessage.Equals("ACK:1"))
                throw new Exception("Ricezione ACK 1 fallita. L'invio della lista di backup al client non è andata a buon fine ");

        }

        /**
         * Method that processes the client request of creating a new backup
         */
        private static void processPostBackup(SslStream sslStream)
        {
            string clientID = null;
            string sessionID = null;

            /* Send ACK:0 */
            sslStream.Write(Encoding.UTF8.GetBytes("ACK:0\r\n"));
            sslStream.Flush();
            Console.WriteLine(">Server: ACK:0");

            /* Receive ClientID AND Session ID */
            string messageRcv = ReadMessage(sslStream, "\r\n"); // "ClientID?SessionID"
            Console.WriteLine(">Client: " + messageRcv);
            string[] stringArray = messageRcv.Split(new Char[] { '?' });
            if (stringArray.Length != 2)
                throw new Exception("Authentication message malformed: " + messageRcv);

            clientID = stringArray[0].ToLower();
            sessionID = stringArray[1].ToLower();

            // Compute sha1 sessionID
            string sha1SessionID = computeSHA1String(sessionID);
            if (sha1SessionID == null)
                throw new Exception("Il metodo computeSHA1String(sessionID) ha restituito null");
            sha1SessionID = sha1SessionID.ToLower();

            /* Check session ID */
            string dbSessionID = DbManager.getSessionID(clientID);
            string responseMessage = null;
            if (dbSessionID.Equals(sha1SessionID))
            {
                /* Send AUTHENTICATION_OK to the client */
                responseMessage = AUTHENTICATION_OK;
                Console.WriteLine(">Server: " + responseMessage);
                responseMessage += "\r\n";
                sslStream.Write(Encoding.UTF8.GetBytes(responseMessage));
                sslStream.Flush();
            }
            else
            {
                /* Send AUTHENTICATION_ERR to the client */
                responseMessage = AUTHENTICATION_ERR;
                Console.WriteLine(">Server: " + responseMessage);
                responseMessage += "\r\n";
                sslStream.Write(Encoding.UTF8.GetBytes(responseMessage));
                sslStream.Flush();
                return;
            }

            /* Receive json string */
            string jsonString = ReadMessage(sslStream, "\r\n"); // "jsonString"
            //Console.WriteLine(">Client (json string): " + jsonString);

            /* Receive timestamp of the screening */
            string timestamp = ReadMessage(sslStream, "\r\n"); // "timestamp"
            Console.WriteLine(">Client: " + timestamp);

            /* Deserialize json */
            List<myFileInfo> fileList = null;
            fileList = JsonConvert.DeserializeObject<List<myFileInfo>>(jsonString);
            if (fileList == null)
                throw new Exception("Error while parsing the json string received");

            if (fileList.Count < 1)
                throw new Exception("La cartella che si desidera sincronizzare è vuota");
                

            // Non ci sono state eccezioni nel parsing del json, quindi la stringa json che mi è stata inviata è consistente. Possiamo salvarla nel DB
            DbManager.insertBackupRecord(clientID, timestamp, jsonString);

            try
            { 
                /* Send ACK:1 */
                sslStream.Write(Encoding.UTF8.GetBytes("ACK:1\r\n"));
                sslStream.Flush();
                Console.WriteLine(">Server: ACK:1");

                List<myFileInfo> fileToDownload = new List<myFileInfo>(); // list of files (realtive path to the client-directory) that the client have to send to the server in order to perform backup

                /* Process the screen sent by the client */
                foreach (myFileInfo file in fileList)
                {
                    /* Check if the file is already up-to-date */
                    string clientDir = Path.Combine(Storage_Path, clientID);
                    string directory = Path.Combine(file.RelativePath, file.Name.TrimStart(Path.DirectorySeparatorChar));
                    // Directory that contain the file server side
                    string directoryPathServer = Path.Combine(clientDir, directory.TrimStart(Path.DirectorySeparatorChar)); // If path2 contains an absolute path, this method returns path2
                                                                                                                            //Console.WriteLine("Checking if the directory '" + directoryPathServer + "\' exists on the server...");
                    if (!Directory.Exists(directoryPathServer)) // Check if the directory related to that file exists: STORAGE_PATH\CLIENT_ID\RELATIVE_PATH_CLIENT\FILE_NAME
                    {
                        // There is no the directory related to the file, hence the file has ever been on the server.
                        // Add the file to the list of files that will be requested to the client.
                        fileToDownload.Add(file);
                        continue; // process the next file
                    }

                    /* The directory exists. Check if the last version of the file on the server is the same of the client */
                    // Get the most recent file saved on the server
                    string mostRecentFilename = getLastFile(directoryPathServer); // FileName: timestamp_last_write
                                                                                  
                    if (mostRecentFilename != null)
                    {
                        string filePathServer = Path.Combine(directoryPathServer, mostRecentFilename); // filePathServer: STORAGE_PATH\CLIENT_ID\RELATIVE_PATH_CLIENT\FILE_NAME\timestamp_last_write

                        /* Compare checksums */
                        string localFileChecksum = computeSHA1Checksum(filePathServer);
                        if (localFileChecksum == null)
                            throw new Exception("Errore nel calcolo del checksum"); 
                        if (!file.Checksum.Equals(localFileChecksum))
                            fileToDownload.Add(file); // the most recent file on the server is different from the file on the client
                    }
                    else
                        // The directory related to the file exists, but it is empty.
                        fileToDownload.Add(file); // Add the file to the list of files that will be requested to the client.
                    
                }
                
                /* Send to client the list of files to download */
                if (fileToDownload.Count > 0)
                {
                    Console.WriteLine("I file che saranno richiesti al client sono i seguenti:");
                    foreach (myFileInfo f in fileToDownload)
                        Console.WriteLine(Path.Combine(f.RelativePath, f.Name));

                    /* Send FILE_LIST */
                    sslStream.Write(Encoding.UTF8.GetBytes(FILE_LIST + "\r\n"));
                    sslStream.Flush();
                    Console.WriteLine("Messaggo inviato: FILE_LIST");

                    /* Create and Send Json file list */
                    string jsonList = JsonConvert.SerializeObject(fileToDownload);
                    sslStream.Write(Encoding.UTF8.GetBytes(jsonList + "\r\n"));
                    sslStream.Flush();
                    Console.WriteLine("Lista dei file da scaricare inviata: " + jsonList);


                    // create a backup copy of the list that may be used to delete the files downloaded if an error occurs
                    List<myFileInfo> backupDownloadList = new List<myFileInfo>(fileToDownload);

                    try
                    {

                        /* Download files */
                        while (fileToDownload.Count > 0)
                        {
                            /* Receive filename (ClientPath + Name) AND sessionID:1 */
                            messageRcv = ReadMessage(sslStream, "\r\n"); //"PATH/NAME?sessionID:1"
                            Console.WriteLine(">Client: " + messageRcv);
                            stringArray = messageRcv.Split(new Char[] { '?' });
                            if (stringArray.Length != 2)
                                throw new Exception("File name message malformed: " + messageRcv);

                            if (!isRightChallenge(sessionID, stringArray[1], "1"))
                                throw new Exception("Challenge 1 sbagliato: " + stringArray[1]);

                            string filePath = stringArray[0]; // Client Path relative to the folder synchronized
                            Console.WriteLine("Filepath ricevuto: " + filePath);
                            string fileName = Path.GetFileName(filePath);
                            string relativePath = Path.GetDirectoryName(filePath);

                            /* Search file in the list */
                            myFileInfo itemFile = fileToDownload.Find(x => x.Name.Equals(fileName) && x.RelativePath.Equals(relativePath)); //TODO: non sono sicuro che se non viene trovato il file nella lista restituisca null
                            if (itemFile == null || itemFile.Name == null)
                                // Client is sending a file that has not been requested
                                throw new Exception("File not requested has been sent");

                            /* File founded in the list. Send ACK:1 */
                            sslStream.Write(Encoding.UTF8.GetBytes("ACK:1\r\n"));
                            sslStream.Flush();
                            Console.WriteLine(">Server: ACK:1");

                            /* Receive file size AND sessionID:2 */
                            messageRcv = ReadMessage(sslStream, "\r\n"); // "size?sessionID:2"
                            Console.WriteLine(">Client: " + messageRcv);
                            stringArray = messageRcv.Split(new Char[] { '?' });
                            if (stringArray.Length != 2)
                                throw new Exception("File size message malformed: " + messageRcv);

                            if (!isRightChallenge(sessionID, stringArray[1], "2"))
                                throw new Exception("Challenge 2 sbagliato: " + stringArray[1]);

                            //Console.Write("Challenge 2 corretto. ");
                            long fileSize = long.Parse(stringArray[0]);

                            Console.WriteLine("Filesize ricevuta : " + fileSize + " Byte");

                            /* Send ACK:2 */
                            sslStream.Write(Encoding.UTF8.GetBytes("ACK:2\r\n"));
                            sslStream.Flush();
                            Console.WriteLine(">Server: ACK:2");

                            /* Receive File */
                            string localFileName = itemFile.TimeStamp + Path.GetExtension(filePath);
                            string newFilePath = Path.Combine(Storage_Path, clientID.TrimStart(Path.DirectorySeparatorChar), filePath.TrimStart(Path.DirectorySeparatorChar), localFileName.TrimStart(Path.DirectorySeparatorChar));
                            // Full Filename: "STORAGE_PATH\CLIENT_ID\RELATIVE_PATH_CLIENT\FILE_NAME(CLIENT_SIDE)\TIMESTAMP_ULTIMA_MODIFICA"
                            Console.WriteLine("Creazione del file: " + newFilePath);
                            if (!receiveFile(sslStream, newFilePath, fileSize))
                                throw new Exception("Errore durante la ricezione del file.");

                            /* Send ACK:3 */
                            sslStream.Write(Encoding.UTF8.GetBytes("ACK:3\r\n"));
                            sslStream.Flush();
                            Console.WriteLine(">Server: ACK:3");

                            /* Receive Checksum AND sessionID:3 */
                            messageRcv = ReadMessage(sslStream, "\r\n"); // "Checksum?sessionID:3"
                            Console.WriteLine(">Client: " + messageRcv);
                            stringArray = messageRcv.Split(new Char[] { '?' });
                            if (stringArray.Length != 2)
                                throw new Exception("Checksum message malformed: " + messageRcv);

                            if (!isRightChallenge(sessionID, stringArray[1], "3"))
                                //Console.WriteLine("Challenge 3 sbagliato");
                                throw new Exception("Challenge 3 sbagliato: " + stringArray[1]);

                            string checksumReceived = stringArray[0];
                            //Console.WriteLine("Checksum ricevuto: '" + checksumReceived + "'");
                            string checksumComputed = computeSHA1Checksum(newFilePath);
                            if (checksumComputed == null)
                                throw new Exception("Errore nel calcolo del checksum lato server");

                            //Console.WriteLine("Checksum del file '" + Path.GetFileName(newFilePath) + "': " + checksumComputed);

                            // Verify checksum received
                            if (!checksumReceived.Equals(checksumComputed))
                                throw new Exception("Checksum ricevuto diverso dal checksum calcolato");
                            
                            /* Send ACK:4 */
                            sslStream.Write(Encoding.UTF8.GetBytes("ACK:4\r\n"));
                            sslStream.Flush();
                            Console.WriteLine(">Server: ACK:4");

                            /*  Transfer successfully completed. Remove file from the list of file to download */
                            fileToDownload.Remove(itemFile);
                        }
                    }
                    catch (Exception e) // se c'è qualche problema durante il download dei file, cancello tutti gli eventuali file già trasferiti
                    {
                        Console.WriteLine("Errore nel backup. Annullamento del backup in corso...");
                        // If an error occurs while downloading the files, undo the backup: delete all the file already downloaded
                        foreach (myFileInfo fi in backupDownloadList)
                        {
                            // compute the local file name
                            string localFileName = fi.TimeStamp + Path.GetExtension(fi.Name);
                            string localFilePath = Path.Combine(Storage_Path, clientID.TrimStart(Path.DirectorySeparatorChar), fi.RelativePath.TrimStart(Path.DirectorySeparatorChar), fi.Name.TrimStart(Path.DirectorySeparatorChar), localFileName.TrimStart(Path.DirectorySeparatorChar));

                            if (File.Exists(localFilePath))
                                File.Delete(localFilePath);
                        }
                        throw e; // rilancio l'eccezione ai livelli superiori
                    }
                }
                else
                    Console.WriteLine("I file sul server sono già aggiornati.");
            }catch(Exception e)
            {
                Console.WriteLine(e.StackTrace);

                // if an error occurs while creating the backup, remove backup record from db
                DbManager.removeBackupRecord(clientID, timestamp);

                throw e; // rilancio l'eccezione ai livelli superiori

            }
            //There are no more file to download

            /* Send Transfer complete message: END_BACKUP */
            sslStream.Write(Encoding.UTF8.GetBytes(END_BACKUP + "\r\n"));
            sslStream.Flush();
            Console.WriteLine(">Server: " + END_BACKUP);

            // Backup completed.
        }

        /**
         * Method that processes the client request of restoring a backup or a portion of it
         */
        private static void processGetBackup(SslStream sslStream)
        {
            string clientID = null;
            string sessionID = null;

            /* Send ACK:0 */
            sslStream.Write(Encoding.UTF8.GetBytes("ACK:0\r\n"));
            sslStream.Flush();
            Console.WriteLine(">Server: ACK:0");

            /* Receive ClientID AND Session ID */
            string messageRcv = ReadMessage(sslStream, "\r\n"); // "ClientID?SessionID"
            Console.WriteLine(">Client: " + messageRcv);
            string[] stringArray = messageRcv.Split(new Char[] { '?' });
            if (stringArray.Length != 2)
                throw new Exception("Authentication message malformed: " + messageRcv);

            clientID = stringArray[0].ToLower();
            sessionID = stringArray[1].ToLower();

            // Compute sha1 sessionID
            string sha1SessionID = computeSHA1String(sessionID);
            if (sha1SessionID == null)
                throw new Exception("Il metodo computeSHA1String(sessionID) ha restituito null");
            sha1SessionID = sha1SessionID.ToLower();

            /* Check session ID */
            string dbSessionID = DbManager.getSessionID(clientID);
            string responseMessage = null;
            if (dbSessionID.Equals(sha1SessionID))
            {
                /* Send AUTHENTICATION_OK to the client */
                responseMessage = AUTHENTICATION_OK;
                Console.WriteLine(">Server: " + responseMessage);
                responseMessage += "\r\n";
                sslStream.Write(Encoding.UTF8.GetBytes(responseMessage));
                sslStream.Flush();
            }
            else
            {
                /* Send AUTHENTICATION_ERR to the client */
                responseMessage = AUTHENTICATION_ERR;
                Console.WriteLine(">Server: " + responseMessage);
                responseMessage += "\r\n";
                sslStream.Write(Encoding.UTF8.GetBytes(responseMessage));
                sslStream.Flush();
                return;
            }

            /* Receive json string */
            string jsonString = ReadMessage(sslStream, "\r\n"); // "jsonString"
            Console.WriteLine("Messaggio ricevuto dal client: " + jsonString);

            /* Deserialize json */
            List<myFileInfo> fileToSend = null;
            fileToSend = JsonConvert.DeserializeObject<List<myFileInfo>>(jsonString);
            if (fileToSend == null)
                throw new Exception("Error while parsing the json string received");

            // In teoria il numero di elementi dovrebbe essere sempre maggiore di zero altrimenti il client non instaura neanche la connessione

            Console.WriteLine("Numero di file da mandare al client: " + fileToSend.Count);

            // Non ci sono state eccezioni nel parsing del json, quindi la stringa json che mi è stata inviata è consistente.

            /* Send ACK:1 */
            sslStream.Write(Encoding.UTF8.GetBytes("ACK:1\r\n"));
            sslStream.Flush();
            Console.WriteLine(">Server: ACK:1");

            /* Send files to Client */
            foreach (myFileInfo mfi in fileToSend)
            {

                string serverFileName = mfi.TimeStamp + Path.GetExtension(mfi.Name);
                string serverFilePath = Path.Combine(Storage_Path, clientID.TrimStart(Path.DirectorySeparatorChar), mfi.RelativePath.TrimStart(Path.DirectorySeparatorChar), mfi.Name.TrimStart(Path.DirectorySeparatorChar), serverFileName.TrimStart(Path.DirectorySeparatorChar));
                // Full Filename: "STORAGE_PATH\CLIENT_ID\RELATIVE_PATH_CLIENT\FILE_NAME\TIMESTAMP_ULTIMA_MODIFICA"
                if (!File.Exists(serverFilePath))
                    throw new Exception("Error: Il file richiesto non esiste sul server. File: " + serverFilePath);

                Console.WriteLine("Il file esiste: " + serverFilePath);
                /* Send filename (RelativePath + Name) */
                string filenameMessage = Path.Combine(mfi.RelativePath, mfi.Name.TrimStart(Path.DirectorySeparatorChar));
                Console.WriteLine(">Server: " + filenameMessage);
                filenameMessage += "\r\n";
                sslStream.Write(Encoding.UTF8.GetBytes(filenameMessage));
                sslStream.Flush();

                /* Receive ACK?sessionID:2 */
                messageRcv = ReadMessage(sslStream, "\r\n"); //"ACK?sessionID:2"
                Console.WriteLine(">Client: " + messageRcv);
                stringArray = messageRcv.Split(new Char[] { '?' });
                if (stringArray.Length != 2)
                    throw new Exception("File name message malformed: " + messageRcv);

                if (!isRightChallenge(sessionID, stringArray[1], "2"))
                    throw new Exception("Challenge 2 sbagliato: " + stringArray[1]);

                if (!stringArray[0].Equals("ACK"))
                    throw new Exception("Ricezione ACK fallita.");

                /* Send file size */
                FileInfo fi = new FileInfo(serverFilePath);
                long fileSize = fi.Length;
                string sizeMessage = fileSize.ToString();
                Console.WriteLine(">Client: " + sizeMessage);
                sizeMessage += "\r\n";
                sslStream.Write(Encoding.UTF8.GetBytes(sizeMessage));
                sslStream.Flush();

                /* Receive ACK?sessionID:3 */
                messageRcv = ReadMessage(sslStream, "\r\n"); //"ACK?sessionID:3"
                Console.WriteLine(">Client: " + messageRcv);
                stringArray = messageRcv.Split(new Char[] { '?' });
                if (stringArray.Length != 2)
                    throw new Exception("File name message malformed: " + messageRcv);

                if (!isRightChallenge(sessionID, stringArray[1], "3"))
                    throw new Exception("Challenge 3 sbagliato: " + stringArray[1]);

                if (!stringArray[0].Equals("ACK"))
                    throw new Exception("Ricezione ACK fallita.");

                /* Send File */
                if (!sendFile(sslStream, serverFilePath, fileSize))
                    throw new Exception("Errore durante l'invio del file");

                Console.WriteLine("File inviato.");

                /* Receive ACK?sessionID:4 */
                messageRcv = ReadMessage(sslStream, "\r\n"); //"ACK?sessionID:4"
                Console.WriteLine(">Client: " + messageRcv);
                stringArray = messageRcv.Split(new Char[] { '?' });
                if (stringArray.Length != 2)
                    throw new Exception("File name message malformed: " + messageRcv);

                if (!isRightChallenge(sessionID, stringArray[1], "4"))
                    throw new Exception("Challenge 4 sbagliato: " + stringArray[1]);

                if (!stringArray[0].Equals("ACK"))
                    throw new Exception("Ricezione ACK fallita.");

                /* Send Checksum */
                string checksum = computeSHA1Checksum(serverFilePath);
                if (checksum == null)
                    throw new Exception("Errore nel calcolo del checksum");
                Console.WriteLine(">Client (checksum): " + checksum);
                string checksumMessage = checksum + "\r\n";
                sslStream.Write(Encoding.UTF8.GetBytes(checksumMessage));
                sslStream.Flush();

                /* Receive ACK?sessionID:5 */
                messageRcv = ReadMessage(sslStream, "\r\n"); //"ACK?sessionID:5"
                Console.WriteLine(">Client: " + messageRcv);
                stringArray = messageRcv.Split(new Char[] { '?' });
                if (stringArray.Length != 2)
                    throw new Exception("File name message malformed: " + messageRcv);

                if (!isRightChallenge(sessionID, stringArray[1], "5"))
                    //Console.WriteLine("Challenge 1 sbagliato");
                    throw new Exception("Challenge 5 sbagliato: " + stringArray[1]);

                if (!stringArray[0].Equals("ACK"))
                    throw new Exception("Ricezione ACK fallita.");

                //File uploaded. 
                //Process the next file.
            }

            /* Send END_BACKUP message */
            sslStream.Write(Encoding.UTF8.GetBytes(END_BACKUP + "\r\n"));
            sslStream.Flush();
            Console.WriteLine(">Server: " + END_BACKUP);
        }


        /**
         * Metodo che invia il file specificato in input  e ritorna 'true' se l'invio è andato a buon fine, altrimenti 'false'
         */
        private static bool sendFile(SslStream outputStream, string filePath, long fileSize)
        {
            FileStream fs = null;
            try
            {
                fs = new FileStream(filePath, FileMode.Open, FileAccess.Read);

                long sentBytes = 0;
                int toSend = 0;
                byte[] data = new byte[BUFFER_DIM];

                while (sentBytes != fileSize)
                {
                    //se la dimensione del file è troppo grande toSend verrà inizializzato con BUFFER_DIM, quindi non ci sono problemi di perdita di informazioni da long a int
                    toSend = (int)Math.Min((fileSize - sentBytes), BUFFER_DIM);

                    //leggo parte del file nel buffer
                    toSend = fs.Read(data, 0, toSend);

                    //invio i dati
                    outputStream.Write(data, 0, toSend);

                    sentBytes += toSend;

                    Array.Clear(data, 0, data.Length);
                }

                outputStream.Flush();

            }
            catch (FileNotFoundException fne)
            {
                Console.WriteLine("File non trovato!");
                Console.WriteLine(fne.StackTrace);
                return false;
            }
            catch (Exception e)
            {
                //Console.WriteLine("Eccezione generica durante l'invio del file");
                Console.WriteLine(e.StackTrace);
                return false;
            }
            finally
            {
                if (fs != null)
                {
                    fs.Flush();
                    fs.Close();
                }

            }
            return true;

        }


        /**
         * Method that retrieves the file named with the most recent timestamp from the folder "directoryPath"
         */
        private static string getLastFile(string directoryPath)
        {

            string mostRecent = null;

            foreach (string filePath in Directory.GetFiles(directoryPath)) // for each file in the directory
            {
                string fileName = Path.GetFileName(filePath);
                if (mostRecent == null)
                    mostRecent = fileName;
                else
                {
                    string fileNameNoExt = Path.GetFileNameWithoutExtension(fileName);
                    string mostRecentNoExt = Path.GetFileNameWithoutExtension(mostRecent);
                    // compare the two file names and get the one that represent the most recent timestamp                    
                    if (DateTime.ParseExact(fileNameNoExt, "yyyyMMddHHmmssffff", CultureInfo.InvariantCulture) > DateTime.ParseExact(mostRecentNoExt, "yyyyMMddHHmmssffff", CultureInfo.InvariantCulture)) // process the file name with no extension
                        mostRecent = fileName;
                }
            }
            return mostRecent;
        }

        /**
         * Method that checks if the challenge is right and if the number linked to the challenge is the expected one
         */
        private static Boolean isRightChallenge(string challenge, string receivedString, string challengeNumber)
        {
            String[] substrings = receivedString.Split(':');
            if (substrings.Length != 2)
                return false;
            if (!challenge.Equals(substrings[0]) || !challengeNumber.Equals(substrings[1]))
                return false;
            return true;
        }

        /**
         * Method that creates a new sessionID starting from the client ID, the password and the current timestamp
         */
        private static string CreateSessionID(string client_id, string password) // throw Exception
        {
            string result = null;

            try
            {
                // current timestamp
                string timeStamp = DateTime.Now.ToString("yyyyMMddHHmmssffff");

                // basic challenge structure
                string sessionID = client_id + password + timeStamp;

                // create final challenge
                using (MD5 md5 = MD5.Create())
                {
                    // byte array representation of that string
                    byte[] challengeByte = md5.ComputeHash(Encoding.UTF8.GetBytes(sessionID));

                    // Convert the byte array to hexadecimal string
                    StringBuilder challengeString = new StringBuilder();
                    for (int i = 0; i < challengeByte.Length; i++)
                    {
                        challengeString.Append(challengeByte[i].ToString("x2"));
                    }
                    result = challengeString.ToString();
                }
            }
            catch (Exception e)
            {
                Console.WriteLine("Exception in CreateSectionID(). Stack Trace: ");
                Console.WriteLine(e.StackTrace);
                return null;
            }

            return result;
        }

        /**
         *  Metodo che calcola il checksum MD5 a partire dal file il cui path è dato in input. 
         *  Ritorna il checksum se tutto va a buon fine, ritorna null in caso di problemi.
         */
        //private static string computeMD5Checksum(string filePath)
        //{
        //    StringBuilder checksumString = null;

        //    try
        //    {
        //        using (MD5 md5 = MD5.Create())
        //        {
        //            //Console.WriteLine("Calcolo del checksum...");
        //            using (FileStream stream = File.OpenRead(filePath))
        //            {
        //                byte[] checksum = md5.ComputeHash(stream);
        //                //Console.WriteLine("Lunghezza del checksum: " + checksum.Length);

        //                // Create a new Stringbuilder to collect the bytes and create a string.
        //                checksumString = new StringBuilder();

        //                // Convert the byte array to hexadecimal string
        //                for (int i = 0; i < checksum.Length; i++)
        //                {
        //                    checksumString.Append(checksum[i].ToString("x2"));
        //                }

        //            }
        //        }
        //    }
        //    catch(Exception e)
        //    {
        //        Console.WriteLine("Exception in computeChecksum('" + filePath +"'). Error Message: " + e.Message);                    
        //        //Console.WriteLine("Stack Trace: "+e.StackTrace);
        //        return null;
        //    }
        //    if (checksumString != null)
        //        return checksumString.ToString();
        //    else
        //        return null;
        //}

        /**
         *  Metodo che calcola il checksum SHA1 a partire dal file il cui path è dato in input. 
         *  Ritorna il checksum se tutto va a buon fine, ritorna null in caso di problemi.
         */
        private static string computeSHA1Checksum(string filePath)
        {
            StringBuilder formatted = null;
            try
            {
                using (FileStream fs = new FileStream(@filePath, FileMode.Open))
                using (BufferedStream bs = new BufferedStream(fs))
                {
                    using (SHA1Managed sha1 = new SHA1Managed())
                    {
                        byte[] hash = sha1.ComputeHash(bs);
                        formatted = new StringBuilder(2 * hash.Length);
                        foreach (byte b in hash)
                        {
                            formatted.AppendFormat("{0:X2}", b);
                        }

                    }
                }
            }
            catch (Exception e)
            {
                Console.WriteLine("Exception in computeChecksum('" + filePath + "'). Error Message: " + e.Message);
                //Console.WriteLine("Stack Trace: "+e.StackTrace);
                return null;
            }
            if (formatted != null)
                return formatted.ToString();
            else
                return null;
        }

        /**
         * Method that receives a file and return 'true' if the transfer is completed successfully, 'false' otherwise
         */
        private static bool receiveFile(SslStream inputStream, string filePath, long fileSize)
        {
            int recBytes = 0;
            long leftBytes = 0;
            int toRead = 0;
            byte[] buff = new byte[BUFFER_DIM];
            FileStream fs = null;

            try
            {
                // Create Directory if it doesn't exist
                if (!Directory.Exists(Path.GetDirectoryName(filePath)))
                    Directory.CreateDirectory(Path.GetDirectoryName(filePath));

                //create empty file or override the existing one
                fs = new FileStream(filePath, FileMode.Create, FileAccess.Write);

                for (leftBytes = fileSize; leftBytes > 0;)
                {
                    //verifico se devo leggere un numero di byte inferiore alla lunghezza del buffer
                    toRead = (int)Math.Min(leftBytes, BUFFER_DIM);

                    recBytes = inputStream.Read(buff, 0, toRead);

                    //verifico se ci sono errori nella ricezione
                    if (recBytes > 0)
                    {
                        //aggiorno il numero di bytes rimasti da leggere
                        leftBytes -= recBytes;

                        fs.Write(buff, 0, recBytes);

                        //Console.WriteLine("\n\t# Download: " + (fileSize - leftBytes) +" / "+fileSize+ " Byte");
                    }
                    else if (recBytes == 0)
                    {
                        //errore di connessione
                        throw new Exception("Connection Error");
                    }

                    Array.Clear(buff, 0, buff.Length);
                }
                Console.WriteLine("# Download File Completato: " + (fileSize - leftBytes) + "/" + fileSize + " Byte");
            }
            catch (Exception e)
            {
                Console.WriteLine("Exception in receiveFile(). Error Message: " + e.Message);
                //Console.WriteLine("Stack Trace: " + e.StackTrace);
                return false;
            }
            finally
            {
                // Nel finally si entra sempre, anche quando ci sono i return nel try o nel catch
                if (fs != null)
                {
                    fs.Flush();
                    fs.Close();
                }
            }
            return true;
        }


        /**
         * Method that receives the message sent from the client (ended with 'endLine') and returns it as a string
         */
        static string ReadMessage(SslStream sslStream, string endLine)
        {
            // Read the  message sent by the client.
            // The client signals the end of the message using the endLine marker.
            byte[] buffer = new byte[2048];
            StringBuilder messageData = new StringBuilder();
            int bytes = -1;
            do
            {
                // Read the client's test message.
                bytes = sslStream.Read(buffer, 0, buffer.Length);

                // Use Decoder class to convert from bytes to UTF8 in case a character spans two buffers.
                Decoder decoder = Encoding.UTF8.GetDecoder();
                char[] chars = new char[decoder.GetCharCount(buffer, 0, bytes)];
                decoder.GetChars(buffer, 0, bytes, chars, 0);
                messageData.Append(chars);
                // Check for endLine or an empty message.
                if (messageData.ToString().IndexOf(endLine) != -1)
                {
                    break;
                }
            } while (bytes != 0);

            //remove endLine
            string messageDataString = messageData.ToString();
            messageDataString = messageDataString.TrimEnd(endLine.ToCharArray());

            return messageDataString;
        }

        /**
        *  Metodo che calcola la stringa SHA1 a partire dalla stringa data in input.
        *  Ritorna la stringa sha1 se tutto va a buon fine, ritorna null in caso di problemi.
        */
        private static string computeSHA1String(string inputString)
        {
            StringBuilder sha1StringBuilder = null;
            try
            {
                using (SHA1Managed sha1 = new SHA1Managed())
                {
                    byte[] hash = sha1.ComputeHash(Encoding.UTF8.GetBytes(inputString));
                    sha1StringBuilder = new StringBuilder(2 * hash.Length);
                    foreach (byte b in hash)
                    {
                        sha1StringBuilder.AppendFormat("{0:X2}", b);
                    }

                }

            }
            catch (Exception e)
            {
                Console.WriteLine("Exception in computeSHA1String('" + inputString + "'). Error Message: " + e.Message);
                //Console.WriteLine("Stack Trace: "+e.StackTrace);
                return null;
            }
            if (sha1StringBuilder != null)
                return sha1StringBuilder.ToString();
            else
                return null;
        }
        
    }
}
