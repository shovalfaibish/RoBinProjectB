using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;

using Communication;
using CommunicationGUI.Utility;
using System.Runtime.InteropServices; // tells us if we're on Windows OR Linux
using System.Globalization;
using System.Threading;
using System.Timers;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Diagnostics;

/*********************************************************************************\
 **********************************************************************************
 *** --VERSION 2.5-- of "Communication".                                        ***
 *** This application is used to for two way communication with the             ***
 *** GroundStation application.                                                 ***
 ***                                                                            ***
 *** FROM THE GROUNDSTATION:                                                    ***
 *** (1) strings representing direct commands from the user, such as "manual",  ***
 *** "external", "DB", "camera", "navigation", etc.                             ***
 *** (2) project files - XMLs representing a sets of missions/tasks inteded for ***
 *** the robin to runl.                                                         ***
 ***                                                                            ***
 *** TO THE GROUNDSTATION:                                                      ***
 *** (1) HeartBeats - short strings meant to signal the groundstation that you  ***
 *** are alive and well, as well as pass on some additional data like location. ***
 *** (2) Files:                                                                 ***
 ***     (A) DB files - the user can ask the robin unit for a copy of its DB.   ***
 ***     (B) Images - from the camera module.                                   ***
 ***     (C) Sensors Data - a txt file that goes with the images sent.          ***
 **********************************************************************************
 \********************************************************************************/

namespace CommunicationGUI
{
    public partial class CommunicationGUI : Form
    {
        bool simulation = false;

        AsyncClient _client;
        object getIpLock = new object();
        Dictionary<int, int> countIpPings;
        private string robinFolder;
        System.Timers.Timer imageDisconnectTimer;
        int reconnectAttempts = 0;
        bool tryingToConnect = false;
        //int reconnectMax = 2; // attempt to connect N times

        // task vars
        CommunicationTask currentTask;
        object task_lock = new object();
        object connectLock = new object();
        int heartBeatFreq;

        // threading
        private bool imageSendingStopped;
        private bool stopImageThread;
        private bool saveLocally;
        Object cameraLock = new object();

        class CommunicationTask
        {
            public int TaskID { get; set; }
            public string Command { get; set; }
            public string FilePath { get; set; }
            public string Data { get; set; }
        }
        List<CommunicationTask> CommunicationTasks = new List<CommunicationTask>();

        // robin name
        string RobinID = string.Empty;
        int delayMS = 50;
        private int msToConnect = 5000;

        // host data
        private System.Timers.Timer hostCheck;
        private string hostAddress = String.Empty;
        private object hostCheckLock = new object();

        public CommunicationGUI()
        {
            InitializeComponent();

            Setup();
        }

        #region Setup Functions

        /// <summary>
        /// used in the setup section to find the groundstation's
        /// IP (we look for it by seeing who's listening on a specific
        /// unique port we know in advance)
        /// </summary>
        /// <returns>the groundstation's IP</returns>
        private async Task<List<string>> getAllConnectedIPs(int attempt)
        {
            IPScanner scanner = new IPScanner();

            // we have a ping counter per attempt because past pings may accidentaly
            // return during a NEW attempt and ruin the ping counter.
            string localIP;
            using (Socket s = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, 0))
            {
                s.Connect("8.8.8.8", 65530);
                System.Net.IPEndPoint e = s.LocalEndPoint as System.Net.IPEndPoint;
                localIP = e.Address.ToString();
            }

            string[] localIPElems = localIP.Split('.');
            string baseIP = $"{localIPElems[0]}.{localIPElems[1]}.{localIPElems[2]}.";
            for (int i = 2; i < 255; i++)
            {
                string ip = baseIP + i.ToString();
                if (ip == localIP) continue;
                Ping p = new Ping();
                p.PingCompleted += new PingCompletedEventHandler(PingCompletedEvent);
                p.SendAsync(ip, 100, (ip, scanner));
            }

            // wait for all IPs to reply OR 2.5 seconds.
            // after that, pass along the IPs we managed to get.
            await Task.Run(() =>
            {
                Stopwatch pingTimer = new Stopwatch();
                pingTimer.Start();
                while (scanner.countIpPings < 253)
                {
                    if (pingTimer.ElapsedMilliseconds > msToConnect / 2) break;
                    Thread.Sleep(100);
                }
                pingTimer.Stop();
            });

#if DEBUG
            foreach (string candidate in scanner.hostIpCandidates)
            {
                Console.WriteLine(candidate); 
            }
#endif

            return scanner.hostIpCandidates;
        }

        async Task<bool> IpAndPortAreOpen(string IP, int port)
        {
            try
            {
                bool connected = false;
                var client = new TcpClient();
                await Task.Run(() =>
                {
                    if (client.ConnectAsync(IP, port).Wait(3000))
                    {
                        connected = true;
                    }
                });
                return connected;
            }
            catch (Exception)
            {
                // the type of exception doesn't matter here.
                // its some kind of connection error.
                return false;
            }
        }

        /// <summary>
        /// An event that raises when the ping response is received (success/timeout).
        /// </summary>
        /// <param name="sender">Sender.</param>
        /// <param name="e">we get our ping data from here</param>
        private void PingCompletedEvent(object sender, PingCompletedEventArgs e)
        {
            ValueTuple<string, IPScanner> data = (ValueTuple<string, IPScanner>)e.UserState;
            string ip = data.Item1;
            IPScanner scanner = data.Item2;
            lock (getIpLock)
            {
                if (e.Reply != null && e.Reply.Status == IPStatus.Success)
                {
                    scanner.hostIpCandidates.Add(ip);
                }
                scanner.countIpPings++;
            }
        }


        /// <summary>
        /// various setup actions in a specific order, to make sure proper start of all parts of the module
        /// </summary>
        private async void Setup()
        {
            SetupLogging();
            SetupGeneral();
            await SetupServer(); // wait for the server to connect before starting threads
            SetupThreads();
            Tracking("Setup", "Finished setup actions.");
        }

        /// <summary>
        /// take care of creating the log file before the first log
        /// </summary>
        private void SetupLogging()
        {
            // windows and linux have different path formats.
            // this part is here bc of testing on windows
            string logBase, LogFilePath;
            robinFolder = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile) + "/RoBin_Files";
            logBase = robinFolder + "/Logs";
            string latestLogDir = logBase + "/Latest";
            if (Directory.Exists(latestLogDir))
            {
                // the log folder was created, create the comm log 
                LogFilePath = latestLogDir + "/Communication_Logs.txt";
                logger.SetLogFilePath(LogFilePath);
            }
            logger.StartSetup();
            logger.ReportLogPath();
        }

        /// <summary>
        /// general setup actions
        /// </summary>
        private void SetupGeneral()
        {
            // form title
            System.Reflection.Assembly assembly = System.Reflection.Assembly.GetExecutingAssembly();
            System.Diagnostics.FileVersionInfo fvi = System.Diagnostics.FileVersionInfo.GetVersionInfo(assembly.Location);
            this.Text = $"RoBin-Communiction V{fvi.FileMajorPart}.{fvi.FileMinorPart} [Build {fvi.FileBuildPart}]";

            // robin name
            string robinFile;
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                robinFile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile) + @"\RoBin_Files\robinSettings.txt";
            }
            else
            {
                robinFile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile) + @"/RoBin_Files/robinSettings.txt";
            }
            if (File.Exists(robinFile))
            {
                string robinSettings = File.ReadAllText(robinFile);
                string[] robinSettingsElems = robinSettings.Split(' ');
                RobinID = robinSettingsElems[0].Trim();
            }

            // tasks
            heartBeatFreq = 5000; // MS between HBs

            Parser.Tracking += Tracking;
            Parser.Error += Error;
            SqlHelper.Tracking += Tracking;
            SqlHelper.Error += Error;

            // camera
            imageDisconnectTimer = new System.Timers.Timer();
            imageDisconnectTimer.Interval = 10000; // 5 sec disconnect timer
            imageDisconnectTimer.Elapsed += imageDisconnectCheck;
            imageDisconnectTimer.AutoReset = false;

            // threading 
            countIpPings = new Dictionary<int, int>();
            imageSendingStopped = false;
            stopImageThread = false;
            saveLocally = false;
            if (simulation)
            {
                this.Text = $"SIMULATION RoBin-Communiction V{fvi.FileMajorPart}.{fvi.FileMinorPart} [Build {fvi.FileBuildPart}]";
                this.BackColor = Color.Blue;
                this.ForeColor = Color.Yellow;
            }
        }

        private void imageDisconnectCheck(object sender, ElapsedEventArgs e)
        {
            // send the manager a task to stop camera operations
            RobinInternal("Camera,Stop");
        }

        /// <summary>
        /// server-related setup actions
        /// </summary>
        private async Task SetupServer()
        {
            tryingToConnect = true;
            if (_client != null)
            {
                // TODO do I need to close anything on reconnection?
            }
            _client = new AsyncClient(robinFolder, RobinID);
            _client.Tracking += Tracking;
            _client.Error += Error;
            _client.RobinSocketIsFree += RobinSocketIsFree;
            _client.ConnectToGround += ConnectToGround;

            // find the groundIP and try to connect to it indefinitely.
            // if you found it, send a "Startup" msg.
            await ConnectToGround();
            tryingToConnect = false;
            // server events
            _client.RobinProject += RobinProject;
            _client.RobinManual += RobinManual;
            _client.RobinExternal += RobinExternal;
            _client.RobinShutdown += RobinShutdown;
            _client.RobinCalibrate += RobinCalibrate;
            _client.RobinSettings += RobinSettings;
            _client.RobinSoftReset += RobinSoftReset;
            _client.RobinSetDate += RobinSetDate;

            // SQL request events
            _client.RobinRequestDB += RobinRequestDB;
            _client.RobinRequestDBClean += RobinRequestDBClean;
            Tracking("SetupServer", "Finished server setup.");
        }

        private async Task ConnectToGround()
        {
            Tracking("ConnectToGround", "Attempting to connect to the ground...");

            reconnectAttempts = 0;
            // look for all network IPs, and try to connect
            // to them at port=10001. we use a unique port, 
            // so only 1 IP is the groundstation IP.
            await ConnectToGroundAttempt();
            System.Threading.Thread.Sleep(100);

            // asyncronously (=non GUI-blocking) wait for the client to connect 
            // to the GroundStation.
            // this waits INDEFINITELY until a connection is made.
            await Task.Run(() =>
            {
                Stopwatch reconnectWatch = new Stopwatch();
                reconnectWatch.Start();
                while (!_client.IsConnected())
                {
                    //if (reconnectWatch.ElapsedMilliseconds > msToConnect)
                    //{
                    //   reconnectWatch.Restart();
                    lock (connectLock)
                    {
                        ConnectToGroundAttempt();
                    }
                    System.Threading.Thread.Sleep(100);
                    //}
                }
            });

            if (!_client.IsConnected()) return;

            // "Startup" msg: send your RobinID to the ground
            _client.Send($"^N1,{RobinID}|");
            hostAddress = _client.GetNetworkName();

            // ping the host every 5 seconds to make sure its alive
            // (otherwise, try to reconnect)
            /*hostCheck = new System.Timers.Timer(5000);
            hostCheck.Elapsed += hostCheckEvent;
            hostCheck.AutoReset = true;
            hostCheck.Enabled = true;*/
        }

        private async void hostCheckEvent(object sender, ElapsedEventArgs e)
        {
            if (!await IpAndPortAreOpen(hostAddress, 10001))
            {
                hostCheck.Stop();
                hostCheck.Dispose();
                await SetupServer();
            }
        }

        private async Task ConnectToGroundAttempt()
        {
            reconnectAttempts++;

            List<string> hostCandidates = await getAllConnectedIPs(reconnectAttempts);
            if (_client.IsConnected()) return;
            foreach (string candidate in hostCandidates)
            {
                // make sure this isn't any random IP connected to the network
                if (!await IpAndPortAreOpen(candidate, 10001)) continue;
                if (_client.SetupClient(candidate, 10001) == string.Empty)
                {
                    break;
                }
            }
        }

        /// <summary>
        /// Setup all threads relevant to the app's function
        /// </summary>
        private void SetupThreads()
        {
            // start a thread that sends HeartBeats to the groundstation
            Task addHeartBeats = Task.Run(() =>
            {
                while (true)
                {
                    try
                    {
                        if (_client.IsConnected())
                        {
                            AddHeartbeat();
                        }
                    }
                    catch (Exception e)
                    {
                        Error("AddHeartbeat", "AddHeartbeat crash", e);
                    }
                    Thread.Sleep(heartBeatFreq);
                }
            });

            // start a thread that watches for possible new tasks
            Task watchForTasks = Task.Run(() =>
            {
                while (true)
                {
                    try
                    {
                        if (_client.IsConnected())
                        {
                            LookForTasks();
                        }
                    }
                    catch (Exception e)
                    {
                        Error("LookForTasks", "LookForTasks crash", e);
                    }
                    Thread.Sleep(delayMS);
                }
            });

            // start a thread that executes added tasks
            Task performTasks = Task.Run(() =>
            {
                while (true)
                {
                    try
                    {
                        if (_client.IsConnected())
                        {
                            PerformTasks();
                        }
                        else if (!tryingToConnect)
                        {
                            ConnectToGround();
                        }
                    }
                    catch (Exception e)
                    {
                        Error("PerformTasks", "PerformTasks crash", e);
                    }
                    Thread.Sleep(delayMS);
                }
            });
        }

        private void AddHeartbeat()
        {
            // build the HB string
            string HBString = string.Empty;

            // get the HB string (timestamp + GPS data)
            string HBData = SqlHelper.GetLatestHBData();
            if (HBData != string.Empty)
            {
                HBString = HBData;
            }
            else
            {
                long timestamp = ((DateTimeOffset)DateTime.Now).ToUnixTimeMilliseconds();
                HBString = $"{timestamp},0,0,0,0,0,0,0";
            }
            // do we need the ground to reply to us
            //HBString += $"{addRepliesToHBs}";

            CommunicationTask newTask = new CommunicationTask()
            {
                TaskID = -1,
                Command = "HB",
                FilePath = "",
                Data = HBString
            };
            lock (task_lock)
            {
                CommunicationTasks.Add(newTask);
            }
        }

        private void LookForTasks()
        {
            // get the integer status from the manager. its bits are boolean values of the different
            // tables (1-changes,0-no changes)
            int? status = SqlHelper.GetModuleJobsStatus();
            if (status == null) return;

            // if theres a new job, take it from the driverTasks queue and "run" it
            // if a current task was canceled, stop it
            if (status == 1)
            {
                // Can a communication task be canceled? not sure.
                // if needed, take the "cancel" part from other modules.

                // get the raw SQL data of the new tasks
                List<string> AddedTasks = SqlHelper.GetAllNewCommOuts();
                if (AddedTasks == null)
                {
                    Error("LookForTasks", "ManagerStatus = 1 but encountered an SQL error");
                    return;
                }
                if (AddedTasks.Count == 0)
                {
                    //Tracking("watchForTasks_Thread", "We were notified that DriverTasks status=1 (changes flag) but no new tasks found for some reason");
#if DEBUG
                    Console.WriteLine("We were notified that DriverTasks status=1 (changes flag) but no new tasks found for some reason");
#endif
                    Error("LookForTasks", "ManagerStatus = 1 but for some reason no new CommOut tasks were found");
                    // TODO this happens a lot for some reason. investigate.
                    return;
                }

                // locking sections that change data structures that are shared between 2
                // different threads. any changes on a given object from 2 different 
                // threads needs to be done carefully.
                lock (task_lock)
                {
                    foreach (string taskElem in AddedTasks)
                    {
                        string[] taskElems = taskElem.Split('|');
                        CommunicationTask newTask = new CommunicationTask()
                        {
                            TaskID = Convert.ToInt32(taskElems[0]),
                            Command = taskElems[1],
                            FilePath = taskElems[2],
                            Data = taskElems[3]
                        };
                        switch (newTask.Command)
                        {
                            case "REQUEST_DB":
                                logger.SendDB();
                                break;
                            case "EXTERNAL_PACKET":
                                logger.ExternalPacket();
                                break;
                            default:
                                break;
                        }
                        CommunicationTasks.Add(newTask);
                    }
                }
                // update the ModuleJobs table to say you've read the tasks to do
                bool updateRes = SqlHelper.UpdateTasksRead();
                if (updateRes == false) return;
            }
        }
        private void PerformTasks()
        {
            // locking sections that change data structures that are shared between 2
            // different threads. any changes on a given object from 2 different 
            // threads needs to be done carefully.
            lock (task_lock)
            {
                // check for a new task
                if (CommunicationTasks.Count > 0)
                {
                    CommunicationTask task = CommunicationTasks[0];
                    // perform the task (with some GUI signaling)
                    if (task == null)
                    {
                        // ha? how?
                        Error("performTasks_Thread", "Something weird happened. threading issues?");
                        return;
                    }

                    currentTask = task;
                    CommunicationTasks.RemoveAt(0);

                    if (task.Command != "HB")
                    {
                        Tracking("performTasks_Thread", $"Started task, movement type {task.Command}");
                    }
                    switch (task.Command)
                    {
                        case "REQUEST_DB":
                            string dbPath = task.FilePath;
                            _client.ConnectSocket(SocketTypes.File);
                            _client.SendFile("GROUND", dbPath, FileTypes.DB);
                            break;
                        case "SEND_IMG":
                            string[] dataElems = task.Data.Split(',');
                            if (dataElems.Length != 3)
                            {
                                Error("performTasks_Thread", $"Wrong amount of data elements for TaskID={task.TaskID}");
                            }
                            else if (dataElems[0] == "Start")
                            {
                                // start a special image sending thread.
                                // this is so we don't interupt the normal server operation
                                //SendImagesThread.Start();
                                saveLocally = Convert.ToBoolean(dataElems[1]);
                                stopImageThread = false;
                                imageSendingStopped = false;
                                _client.ConnectCameraSockets();
                                // Task.Factory.StartNew(() => SendImagesToGround(saveLocally));
                                //_client.AddCameraPorts(RobinID, 2);
                                imageDisconnectTimer.Start(); // start a disconnect timer
                                //hostCheck.Enabled = false; // this can interupt the camera
                                RobinSocketIsFree();
                                Task t = new Task(() => finishImageSendingProcess());
                                t.Start();
                            }
                            else if (dataElems[0] == "Stop")
                            {
                                // signal closing actions and immediatly close all camera ports
                                imageDisconnectTimer.Stop(); // no need to check for disconnects anymore
                                stopImageThread = true;
                                //hostCheck.Enabled = true; // return to host checking
#if DEBUG
                                Console.WriteLine("STOP IMG");
#endif
                                //addRepliesToHBs = true;
                            }
                            break;
                        case "SETTINGS":
                            _client.Send($"^T{currentTask.Data}|");
                            break;
                        case "HB":
                            _client.Send($"^H{task.Data}|");
                            break;
                        case "EXTERNAL_PACKET":
                            // notify ground that an external packet was processed
                            _client.Send("^E|");
                            break;
                        case "SOFT_RESET":
                            // notify ground that a softreset was complete
                            _client.Send("^R|");
                            break;
                        default:
                            break;
                    }

                    // update the current task 
                    if (currentTask != null)
                    {
                        bool sqlRes = SqlHelper.UpdateTaskToDone(currentTask.TaskID);
                    }
                    // do I need to update manager that I sent the msgs? does manager care?
                    // if so, return this and implement UpdateTaskToDone.
                }
            }
        }

        class CompareImageNamesByIndex : IComparer<string>
        {
            public int Compare(string s1, string s2)
            {
                string f1 = Path.GetFileName(s1);
                string f2 = Path.GetFileName(s2);
                string f1noEnd = (f1.Split('.'))[0];
                string f2noEnd = (f2.Split('.'))[0];
                int f1ind = Convert.ToInt32(f1noEnd.Split('_')[0]);
                int f2ind = Convert.ToInt32(f2noEnd.Split('_')[0]);
                return Math.Sign(f1ind - f2ind);
            }
        }


        //private void SendImagesToGround(bool saveLocally)
        private void RobinSocketIsFree()
        {
            // we got a confirmation, stop the timer that is meant to 
            // stop all camera confirmations bc we know we have
            // contact with the ground

            imageDisconnectTimer.Stop();
            Task t = new Task(() => SendImagesToGround());
            t.Start();
        }
        private async Task SendImagesToGround()
        {
            if (stopImageThread) return;

            robinFolder = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile) + @"/RoBin_Files/";
            string folderBase = robinFolder + @"/Images/";
            string tmpSendFolder = folderBase + "/Tmp_Send/";
            // this folder has images that weren't queued to send, but were saved anyway
            string tmpStorageFolder = folderBase + "/Image_storage/";

            //try
            //{
            while (!Directory.Exists(folderBase)) { } // wait until the camera thread creates this directory
            Directory.CreateDirectory(tmpSendFolder);
            CompareImageNamesByIndex fileComparer = new CompareImageNamesByIndex();
            //while (!_client.AreAllCameraSocketsBusy( tmp
            while (true)
            {
                string fileName = string.Empty;
                // stop all camera threads
                if (stopImageThread) break;
                // sort the images by index (the index is included in the file's name)
                var images = Directory.GetFiles(folderBase).OrderByDescending(f => f, fileComparer);
                // keep going until we start getting images (or we are ordered to stop)
                if (images.Count() == 0) continue;
                imageDisconnectTimer.Stop(); // tmp
                string image = images.First();
                // move the image to a special tmp folder (so we dont send the same images again)
                fileName = Path.GetFileName(image);
                if (fileName == "sensors_data.txt") continue;
                lock (cameraLock)
                {
                    // could happen, many threads are sending concurrently so some "disappear"
                    if (!File.Exists(folderBase + fileName)) continue;
                    File.Move(folderBase + fileName, tmpSendFolder + fileName);
                }
                // send the file to the ground
                _client.SendImage("GROUND", tmpSendFolder + fileName);

                // delete the file, we don't need it anymore
                File.Delete(tmpSendFolder + fileName);

                // we just sent an image. start a new 5 sec disconnection timer.
                // this can only be stopped if we recieve a confirmation from the ground.
                imageDisconnectTimer.Start();
            }
            // if we don't need to stop yet, don't continue to the next station
            return;
        }

        private void finishImageSendingProcess()
        {
            while (!stopImageThread)
            {
                System.Threading.Thread.Sleep(1000);
            }

            Tracking("finishImageSendingProcess", "Image sending thread finished running");

            robinFolder = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile) + @"/RoBin_Files/";
            string folderBase = robinFolder + @"/Images/";
            string tmpSendFolder = folderBase + "/Tmp_Send/";
            // this folder has images that weren't queued to send, but were saved anyway
            string tmpStorageFolder = folderBase + "/Image_storage/";

            // wait for all the last files to finish sending, then send the end signal
            while (_client.AreAllCameraSocketsFree() == false) { System.Threading.Thread.Sleep(100); }
            _client.CloseAllCameraSockets();
            _client.Send("^C|");

            // try deleting up to a limit of tries
            int tryDelete = 0;
            int MaxTries = 50;
            while (true)
            {
                try
                {
                    // delete the contents, the user doesnt want them
                    if (Directory.Exists(tmpSendFolder)) Directory.Delete(tmpSendFolder, true);
                    if (Directory.Exists(tmpStorageFolder)) Directory.Delete(tmpStorageFolder, true);
                    // we don't want to use dir.delete/create here because it can ruin 
                    // directory access rights for the camera module (which uses it).
                    // we don't know who creates the dir first - camera or comm
                    // (and for some reason creating a dir in this module locks it)
                    if (saveLocally)
                    {
                        while (!File.Exists(folderBase + "sensors_data.txt")) { }
                    }
                    foreach (string file in Directory.GetFiles(folderBase))
                    {
                        if (saveLocally && Path.GetFileName(file) == "sensors_data.txt")
                        {
                            _client.ConnectSocket(SocketTypes.File);
                            _client.SendFile("GROUND", file, FileTypes.ImageSensorsData);
                        }
                        else
                        {
                            File.Delete(file);
                        }
                    }
                    break;
                }
                catch (Exception ex)
                {
                    Error("finishImageSendingProcess", ex.Message);
                    if (tryDelete > MaxTries) break;
                    tryDelete++;
                    System.Threading.Thread.Sleep(100);
                }
            }
            return;
        }

        #endregion

        #region Server related functions

        /// <summary>
        /// used to initiate an internal robin command.
        /// updates the relevant SQL tables.
        /// </summary>
        /// <param name="data">the command data</param>
        /// <returns></returns>
        private async Task RobinInternal(string data)
        {
            try
            {
                // update the CommunicationIn&ModuleJobs sql tables
                int statusNumber = GetStatusNumber(true);
                bool result = await SqlHelper.UpdateCommandReceived(statusNumber, "INTERNAL", "", data);
                if (result == false)
                {
                    // TODO should I do anything here? notify the Ground perhaps?
                    return;
                }
            }
            catch (Exception e)
            {
                Error("RobinInternal", "Problem adding a new internal command update", e);
            }
            //logger.NewCommand(data);
        }

        /// <summary>
        /// used after recieving a Manual command from the ground.
        /// updates the relevant SQL tables.
        /// </summary>
        /// <param name="data">the command data</param>
        /// <returns></returns>
        private async Task RobinManual(string data)
        {
            try
            {
                // update the CommunicationIn&ModuleJobs sql tables
                int statusNumber = GetStatusNumber(true);
                bool result = await SqlHelper.UpdateCommandReceived(statusNumber, "MANUAL", "", data);
                if (result == false)
                {
                    // TODO should I do anything here? notify the Ground perhaps?
                    return;
                }
            }
            catch (Exception e)
            {
                Error("RobinManual", "Problem adding a new manual command update", e);
            }
            logger.NewCommand(data);
        }

        /// <summary>
        /// used after recieving an External command from the ground.
        /// updates the relevant SQL tables.
        /// </summary>
        /// <param name="data">the command data</param>
        /// <returns></returns>
        private async Task RobinExternal(string data)
        {
            try
            {
                // determine wether the current packet is done
                bool raiseModuleJobsFlag = false;
                string[] dataElems = data.Split(',');
                if (dataElems[0] == dataElems[1]) raiseModuleJobsFlag = true;

                // update the CommunicationIn&ModuleJobs sql tables
                int statusNumber = GetStatusNumber(raiseModuleJobsFlag);
                bool result = await SqlHelper.UpdateCommandReceived(statusNumber, "EXTERNAL", "", data);
                if (result == false)
                {
                    // TODO should I do anything here? notify the Ground perhaps?
                    return;
                }
            }
            catch (Exception e)
            {
                Error("RobinExternal", "Problem adding a new external update", e);
            }
            logger.NewExternal(data);
        }

        /// <summary>
        /// used after recieving a Calibrate command from the ground.
        /// updates the relevant SQL tables.
        /// </summary>
        /// <param name="data">the command data</param>
        /// <returns></returns>
        private async Task RobinCalibrate(string data)
        {
            try
            {
                // update the CommunicationIn&ModuleJobs sql tables
                int statusNumber = GetStatusNumber(true);
                bool result = await SqlHelper.UpdateCommandReceived(statusNumber, "CALIBRATE", "", data);
                if (result == false)
                {
                    // TODO should I do anything here? notify the Ground perhaps?
                    return;
                }
            }
            catch (Exception e)
            {
                Error("RobinCalibrate", "Problem adding a new calibrate update", e);
            }
            logger.NewCalibrate(data);
        }

        /// <summary>
        /// used after recieving a Project from the ground.
        /// save the data as an XML file and update the relevant SQL tables.
        /// </summary>
        /// <param name="data"></param>
        /// <returns></returns>
        private async Task RobinProject(string data)
        {
            DateTime date = DateTime.Now;
            string suffix = date.ToString("ddMMyyHHmmssffff");
            string unknownXmlPath;
            // the "slash" direction is different between Windows and Linux..
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                unknownXmlPath = robinFolder.Replace(@"\", @"\\") + $"Unknown\\\\Unknown{suffix}.xml";
            }
            else
            {
                unknownXmlPath = robinFolder + $"Unknown//Unknown{suffix}.xml";
            }


            //WriteFileAync(unknownXmlPath, projectData);
            try
            {
                // write project file
                using (StreamWriter writer = File.CreateText(unknownXmlPath))
                {
                    await writer.WriteAsync(data);
                }

                // update the CommunicationIn&ModuleJobs sql tables
                int statusNumber = GetStatusNumber(true);
                bool result = await SqlHelper.UpdateCommandReceived(statusNumber, "PROJECT", unknownXmlPath, "");
                if (result == false)
                {
                    // TODO should I do anything here? notify the Ground perhaps?
                    return;
                }
            }
            catch (Exception e)
            {
                Error("RobinProject", "Problem adding a new project update", e);
            }
            logger.NewProject(unknownXmlPath);
        }

        /// <summary>
        /// used after recieving a request for DB from the ground.
        /// update the relevant SQL tables.
        /// </summary>
        /// <returns></returns>
        private async Task RobinRequestDB()
        {
            try
            {
                // update the CommunicationIn&ModuleJobs sql tables
                int statusNumber = GetStatusNumber(true);
                bool result = await SqlHelper.UpdateCommandReceived(statusNumber, "REQUEST_DB", "", "");
                if (result == false)
                {
                    // TODO should I do anything here? notify the Ground perhaps?
                    return;
                }
            }
            catch (Exception e)
            {
                Error("RobinRequestDB", "Problem adding a new DB request", e);
            }
            logger.RequestDB();
        }

        /// <summary>
        /// used after recieving a DB cleaning request from the ground.
        /// updates the relevant SQL tables.
        /// </summary>
        /// <returns></returns>
        private async Task RobinRequestDBClean()
        {
            try
            {
                // update the CommunicationIn&ModuleJobs sql tables
                int statusNumber = GetStatusNumber(true);
                bool result = await SqlHelper.UpdateCommandReceived(statusNumber, "REQUEST_CLEAN", "", "");
                if (result == false)
                {
                    // TODO should I do anything here? notify the Ground perhaps?
                    return;
                }
            }
            catch (Exception e)
            {
                Error("RobinRequestDBClean", "Problem adding a new DB cleaning request", e);
            }
            logger.RequestDB();
        }

        /// <summary>
        /// used after recieving a settings change from the ground.
        /// updates the relevant SQL tables.
        /// </summary>
        /// <param name="data"></param>
        /// <returns></returns>
        private async Task RobinSettings(string data)
        {
            try
            {
                // update the CommunicationIn&ModuleJobs sql tables
                int statusNumber = GetStatusNumber(true);
                bool result = await SqlHelper.UpdateCommandReceived(statusNumber, "SETTINGS", "", data);
                if (result == false)
                {
                    // TODO should I do anything here? notify the Ground perhaps?
                    return;
                }
            }
            catch (Exception e)
            {
                Error("RobinSettings", "Problem adding a new Settings update", e);
            }
            logger.UpdateSettings(data);
        }

        /// <summary>
        /// used after recieving a shutdown command from the ground.
        /// updates the relevant SQL tables.
        /// </summary>
        /// <returns></returns>
        private async Task RobinShutdown()
        {
            // TODO send the shutdown order through the SQL
            try
            {
                // update the CommunicationIn&ModuleJobs sql tables
                int statusNumber = GetStatusNumber(true);
                bool result = await SqlHelper.UpdateCommandReceived(statusNumber, "SHUTDOWN", "", "");
                if (result == false)
                {
                    // TODO should I do anything here? notify the Ground perhaps?
                    return;
                }
            }
            catch (Exception e)
            {
                Error("RobinShutdown", "Problem adding a shutdown order to SQL", e);
            }
            logger.Shutdown();
        }

        /// <summary>
        /// this soft reset goes through all the robin's SQL tables, lowers all flags to 0
        /// and moves all new/running tasks to "done_reset" status.
        /// this is done to try and remove and "locks" the system ran into,
        /// and its done in the communication module because its the FIRST module to 
        /// speak to the ground (directly through a socket, before all the SQL stuff).
        /// </summary>
        /// <returns></returns>
        private async Task RobinSoftReset()
        {
            // module jobs reset
            bool res = await SqlHelper.UpdateModuleJobsStatus(0, 0, true);

            // reset all moduleTasks tables
            List<string> tablesToReset = new List<string>()
            {
                "maintenancetasks", "communicationin", "communicationout",
                "drivertasks", "cranetasks", "cameratasks", "navigationtasks",
                "navigationrequests",
            };
            foreach (string table in tablesToReset)
            {
                res = res && await SqlHelper.ResetTable(table);
            }

            // tell Manager to cancel any ongoing project
            res = await SqlHelper.UpdateCommandReceived(1, "SOFT_RESET", "", "");
        }

        /// <summary>
        /// recieves the time and date from the ground, this way the ground and the 
        /// robot are sufficiently sync'd (other than the delay of the time it takes 
        /// for the ground to send the msg, and for the robin to analyze it).
        /// TODO should we add a delay to the ground to anticipate it? check the avg time to send.
        /// </summary>
        /// <param name="dateString"></param>
        /// <returns></returns>
        private async Task RobinSetDate(string dateString)
        {
            string[] dateStrings = dateString.Split(',');
            if (dateStrings.Length != 2) return;
            await Task.Run(() =>
            {
                if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                {
                    // clean the DB
                    Process p = new Process
                    {
                        StartInfo = new ProcessStartInfo
                        {
                            FileName = "/bin/bash",
                            Arguments = robinFolder + @"/Scripts/setDate.sh " + $"{dateStrings[0]} {dateStrings[1]}",
                            UseShellExecute = false,
                            RedirectStandardOutput = true,
                            CreateNoWindow = true
                        }
                    };
                    p.Start();
                    p.WaitForExit();

                    // log the current datetime in a txt file (this will serve as
                    // this folder's name when a new session starts and needs the "Latest")
                    DateTime date = DateTime.Now;
                    string newDirName = date.ToString("yyMMdd_HHmmss");
                    string latestLogDir = robinFolder + $"/Logs/Latest";
                    if (Directory.Exists(latestLogDir))
                    {
                        File.WriteAllText(latestLogDir + $"/Latest.txt", newDirName);
                    }
                }
            });
        }
        #endregion

        #region Misc

        /// <summary>
        /// returns a status number that corresponds 1-1 with a natural number
        /// </summary>
        /// <param name="CommunicationInChange"> are there changes in the CommunicationIn table</param>
        private int GetStatusNumber(bool CommunicationInChange)
        {
            //TODO : if there are more tables to consider, add them in the signiture
            return Convert.ToInt32(CommunicationInChange);
        }

        /// <summary>
        /// create a new log folder for all the modules to use
        /// </summary>
        /// <param name="basePath"></param>
        /// <returns></returns>
        private string CreateLogFolder(string basePath)
        {
            // create the directory
            DateTime date = DateTime.Now;
            Directory.CreateDirectory(basePath + @"/Latest");

            // create a file with the directory's date
            File.Create(basePath + @"/Latest/Latest.txt");
            string result = date.ToString("ddMMyyHHmmss");
            File.WriteAllText(basePath + @"/Latest/Latest.txt", result);
            return basePath + result;
        }

        /// <summary>
        /// Add a general "tracking" message to the log
        /// </summary>
        /// <param name="function">the function you call this from</param>
        /// <param name="Msg">the message</param>
        public void Tracking(string function, string Msg)
        {
            logger.Tracking(function, Msg);
        }

        /// <summary>
        /// Add an "error" message to the log
        /// </summary>
        /// <param name="function">the function in which the error occured</param>
        /// <param name="msg">an error message</param>
        /// <param name="e">an exception, if one happened</param>
        public void Error(string function, string msg, Exception e = null)
        {
            logger.Error(function, msg, e);
        }

        internal void Application_ThreadException(object sender, ThreadExceptionEventArgs e)
        {
            Error("ThreadApplication", "Unknown Exception raised", e.Exception);
        }

        #endregion
    }
}
