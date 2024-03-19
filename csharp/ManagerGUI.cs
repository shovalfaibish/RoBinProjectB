using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;

using System.Configuration;
using DimensionEngineering;
using System.IO.Ports; // for SerialPort
using System.Data.SqlClient;

//using Communication;
using ManagerGUI.Utility;
using System.Threading;
using System.IO;
using System.Globalization;
using System.Runtime.InteropServices; // tells us if we're on Windows OR Linux
using System.Diagnostics;

/*********************************************************************************\
 **********************************************************************************
 *** --VERSION 2.7-- of "Manager".                                              ***
 *** This application is used to create, ditribute and monitor all the rest     ***
 *** of the modules that the robin uses.                                        ***
 ***                                                                            ***
 *** AUTONOMOUS ACTIONS:                                                        ***
 *** right now, does two autonomous "starting" actions:                         *** 
 *** (1) Bring the crane arm to a safe "Home" position.                         ***
 *** (2) Move the robot left and right quickly (signals that its working)       ***
 *** in the future this part of manager will probably get seperated to 2        ***
 *** parts, a "perform and monitor" manager and a "thinking" manage.            ***
 ***                                                                            ***
 *** GROUND ACTIONS:                                                            ***
 *** we recieve the action strings from the communication module. this includes ***
 *** actions such as: manual, external, custom project (XML file), settings     ***
 *** and requests.                                                              ***
 *** these strings get turned into Projects, the main object that is used to    ***
 *** describe a job., and the projects get run task by task.                    ***
 *** From there, the manager needs to monitor the project status and keep it    ***
 *** going correctly and resolve any issues the modules run into.               ***
 ***                                                                            ***
 *** the manager's main SQL table to look at is "ModuleJobs", and all the       ***
 *** robin's modules update their activity there.                               ***
 ***                                                                            ***
 **********************************************************************************
 \********************************************************************************/

namespace ManagerGUI
{
    public partial class ManagerGUI : Form
    {
        #region Variables

        bool simulation = false;

        //Sabertooth saber = null;
        SerialPort LynxPort;
        //string ConnectionString;
        //AsyncClient _client;

        // project/command vars
        Project currentProject;
        Mission currentMission;
        MissionTask currentTask;
        int CurrentMissionInd = 0;
        int CurrentTaskInd = -1; // CurrentTaskInd starts at (-1) to be able to start the first task
        string currentManual = string.Empty;
        object projChangeLock = new object();

        // settings changes from the ground
        string GroundSettingsTaskID;
        string CurrentCameraTaskID;
        string CurrentNavigationTaskID;
        double cameraCaptureSensorsSampleRate;
        double regularSensorsSampleRate;

        // sql checkers
        System.Threading.Timer timerCheckAllModules; // regular updates from modules
        System.Threading.Timer timerCheckForCommands; // quick response to commands from the ground
        int delayMS = 500;

        // task manager vars
        bool changeInTasks = false;
        bool checkingModules = false;
        HashSet<string> usedTaskIDs;

        // robin name
        string RobinID = string.Empty;

        // files
        string robinFolder;
        string projectBase;

        #endregion

        public ManagerGUI()
        {
            InitializeComponent();

            Setup();
        }

        #region Setup Functions

        /// <summary>
        /// various setup actions in a specific order, to make sure proper start of all parts of the module
        /// </summary>
        private void Setup()
        {
            SetupLogging();
            SetupGeneral();
            SetupThreads();

            // startup sequence for modules
            StartupActions();
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
                LogFilePath = latestLogDir + "/Manager_Logs.txt";
                logger.SetLogFilePath(LogFilePath);
            }
            logger.StartSetup(simulation);
            logger.ReportLogPath();
        }

        /// <summary>
        /// any non-specific setup actions
        /// </summary>
        private async void SetupGeneral()
        {
            // form title
            System.Reflection.Assembly assembly = System.Reflection.Assembly.GetExecutingAssembly();
            System.Diagnostics.FileVersionInfo fvi = System.Diagnostics.FileVersionInfo.GetVersionInfo(assembly.Location);
            this.Text = $"RoBin-Manager V{fvi.FileMajorPart}.{fvi.FileMinorPart} [Build {fvi.FileBuildPart}]";

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
                string craneVals = File.ReadAllText(robinFile);
                string[] craneValsElems = craneVals.Split(' ');
                RobinID = craneValsElems[0];
            }

            // general GUI vars
            btnForward.BackgroundImageLayout = ImageLayout.Stretch;
            currentProject = null;
            currentMission = null;
            currentTask = null;

            /** Manual Tasks that need follow-up **/
            // keep track of a settings change task
            // that the ground requested
            GroundSettingsTaskID = null; // TODO perhaps turn this into a "Task" structure
            CurrentCameraTaskID = null;
            CurrentNavigationTaskID = null;

            usedTaskIDs = new HashSet<string>();
            // logging
            XmlHelper.Tracking += Tracking;
            XmlHelper.Error += Error;

            // initialize port names
            foreach (string port in System.IO.Ports.SerialPort.GetPortNames())
            {
                comboSaberPorts.Items.Add(port);
                comboLynxPorts.Items.Add(port);
            }

            // initialize default sensors sample values
            cameraCaptureSensorsSampleRate = 1.0 / 6.0;
            regularSensorsSampleRate = 5.0;

            // SIMULATION!
            if (simulation)
            {
                this.Text = $"SIMULATION RoBin-Manager V{fvi.FileMajorPart}.{fvi.FileMinorPart} [Build {fvi.FileBuildPart}]";
                this.BackColor = Color.Blue;
            }
        }

        /// <summary>
        /// start the long running threads that look for stuff to do, and do the stuff
        /// </summary>
        private void SetupThreads()
        {
            // after 1 second, this timer will run every 5 seconds and check all modules and perform 
            // the nessecary actions.
            //timerCheckAllModules = new System.Threading.Timer(CheckAllModules, null, 1000, 250);
            //timerCheckAllModules = new System.Threading.Timer(CheckAllModules, null, 1000, 500);

            // sqlhelper events for logging
            SqlHelper.Tracking += Tracking;
            SqlHelper.Error += Error;

            // thread to continuously look for communication with the other modules
            Task.Run(() =>
            {
                while (true)
                {
                    try
                    {
                        ModuleHandler();
                    }
                    catch (Exception e)
                    {
                        Error("ModuleHandler", "ModuleHandler crash", e);
                    }
                    Thread.Sleep(delayMS);
                }
            });

            // thread to handle the current project
            Task.Run(() =>
            {
                while (true)
                {
                    try
                    {
                        ProjectHandler();
                    } catch (Exception e)
                    {
                        Error("ProjectHandler", "ProjectHandler crash", e);
                    }
                    Thread.Sleep(delayMS);
                }
            });
        }

        /// <summary>
        /// Any task that needs to occur as a normal part of 
        /// the RoBin startup sequence will appear here.
        /// </summary>
        private void StartupActions()
        {
            Project startup = new Project(GetID(), ProjectType.ROBIN_STARTUP, 0, RobinID, RobinID, new List<Mission>());

            /* Mission #1: Move crane arm to HOME position: */
            Mission mission1 = new Mission(1, "Crane_To_HOME", new List<MissionTask>());
            startup.AddMission(mission1);

            // Task #1: wrist to (15)
            //mission1.AddTask(new CraneTask(CraneTypes.Wrist, 15));

            // Task #2: claw to -80
            //mission1.AddTask(new CraneTask(CraneTypes.Claw, -30));

            // Task #3: arm to (-90)
            //mission1.AddTask(new CraneTask(CraneTypes.Arm, -90));

            // Task #4: Driver Left 3 degrees (Driver lifesign event #1/2)
            mission1.AddTask(new DriverTask(DriverTypes.Left, 3, 0, 1, 0));

            // Task #5: Driver Right 3 degrees (Driver lifesign event #2/2)
            mission1.AddTask(new DriverTask(DriverTypes.Right, 3, 0, 2, 0));


            // start the mission
            currentProject = startup;
            currentMission = null;
            currentTask = null;

            // logs
            Tracking("StartupActions", "Started startup-project.");
            ManagerActionTypeProject(startup);
        }

        /// <summary>
        /// Used to create a unique ID for the log folder name
        /// </summary>
        /// <returns>a unique date string</returns>
        private string GetID()
        {
            string result = string.Empty;
            DateTime date = DateTime.Now;
            result = date.ToString("ddMMyy_HHmmssffff");
            return result;
        }

        #endregion

        #region Task processing

        /// <summary>
        /// check all module data from ModuleJobs, doing different checks for different modules.
        /// NOTE: that this is "async void" and not "async Task" because event handlers only take async void. 
        /// in other cases async Task is better (bc of some exception handling stuff).
        /// </summary>
        /// <param name="state"> this is an obligatory param for threading.timer </param>
        private void ModuleHandler()
        {
            try
            {
                List<string> moduleData = SqlHelper.GetAllModuleData();
                if (moduleData == null) return;

                foreach (string line in moduleData)
                {
                    string[] elem = line.Split('|');
                    string ModuleName = elem[0];
                    //int ManagerStatus = Convert.ToInt32(elem[1]);
                    int ModuleStatus = Convert.ToInt32(elem[2]);
                    string BinaryModuleStatus = GetStatus(ModuleStatus, 2);
                    bool res;

                    // find out which module we are on
                    switch (ModuleName)
                    {
                        case "Communication":
                            // check for new communication
                            res = CheckCommunication(BinaryModuleStatus);
                            if (!res) continue;
                            break;
                        case "Driver":
                            res = CheckDriver(BinaryModuleStatus);
                            if (!res) continue;
                            break;
                        case "Crane":
                            res = CheckCrane(BinaryModuleStatus);
                            if (!res) continue;
                            break;
                        case "Sensors":
                            res = CheckSensors(BinaryModuleStatus);
                            if (!res) continue;
                            break;
                        case "Camera":
                            res = CheckCamera(BinaryModuleStatus);
                            if (!res) continue;
                            break;
                        case "Navigation":
                            res = CheckNavigation(BinaryModuleStatus);
                            if (!res) continue;
                            break;
                        default:
                            break;
                    }
                    // if the module status number wasnt 0 (0 means no changes at all), set it back to 0
                    if (ModuleStatus != 0)
                    {
                        bool updateRes = SqlHelper.UpdateModuleChecked(elem[0]);
                        //TODO continue? break?? retry? debate this later.
                        if (updateRes == false) continue;
                    }
                }
            }
            finally
            {
            }

            // convert a statusNumber integer to a binary string with [tableCount]-places
            string GetStatus(int statusNumber, int tableCount)
            {
                int statusNumberConverted = Convert.ToInt32(statusNumber);
                string binaryNum = Convert.ToInt32(Convert.ToString(statusNumberConverted, 2)).ToString($"D{tableCount}");
                // reverse to access bits from right to left
                return new string(binaryNum.Reverse().ToArray());
            }
        }

        /// <summary>
        /// check for changes sent from the Communication module
        /// </summary>
        /// <param name="BinaryModuleStatus"></param>
        /// <returns></returns>
        private bool CheckCommunication(string BinaryModuleStatus)
        {
            // modulestatus binary string: [0]: CommunicationIn, 
            if (BinaryModuleStatus[0] == '1')
            {
                // get all new msgs from CommunicationIn table
                List<string> msgData = SqlHelper.GetAllNewMsgs();
                if (msgData == null) return false; //TODO continue or break? decide.

                // iterate through the new msgs and perform the nessecary action
                List<int> processedIDs = new List<int>();
                foreach (string msgLine in msgData)
                {
                    string[] msgElem = msgLine.Split('|');
                    int ID = Convert.ToInt32(msgElem[0]);
                    string Type = msgElem[1];
                    string filePath = msgElem[2];
                    string data = msgElem[3];
                    processedIDs.Add(ID);

                    // Recieved a project from Communication
                    if (Type == "PROJECT")
                    {
                        Project newProject = XmlHelper.CreateProjectFromXml("", projectBase, filePath);
                        if (newProject == null) continue; // TODO determine what to do. abort? continue? retry? msg the Ground?

                        // project data received from the ground can be assumed to have similar ID
                        lock (projChangeLock)
                        { // lock before chaning the vars shared between the threads
                            if (currentProject == null || newProject.ProjectID != currentProject.ProjectID)
                            {
                                // a new project
                                currentProject = newProject;
                                currentMission = null;
                                currentTask = null;

                                // TODO I just replace projects. more actions? cancel old? priority? log?
                                Tracking("CheckAllModules", $"Started a new project, ID={currentProject.ProjectID}");
                            }
                            else
                            {
                                // updating an existing project
                                int missionCount = currentProject.Missions.Count;
                                foreach (Mission mission in newProject.Missions)
                                {
                                    // the mission is added AFTER current missions in the project
                                    mission.MissionStep = missionCount + 1;
                                    missionCount++;
                                    currentProject.AddMission(mission);
                                }

                                // logs
                                Tracking("CheckAllModules", $"Updated the project, ID={currentProject.ProjectID}");
                            }

                            // action log 
                            ManagerActionTypeProject(newProject);

                            // project processed, remove the project-file from the temp folder
                            System.IO.File.Delete(filePath);
                        }
                    }
                    // recieved a Manual-Command from Communication
                    else if (Type == "MANUAL")
                    {
                        // manual controls from the ground for driver/crane

                        // immediatly start the command
                        StopCurrenProject(); // manual commands overwrite projects
                        currentManual = data;
                        StartManual(); // start the manual command
                    }
                    // recieved an External-Command from Communication
                    else if (Type == "EXTERNAL")
                    {
                        // External commands area commands created by external means,
                        // in the Ground-Station PC (like MATLAB) 


                        // TODO do any external commands override projects??
                        parseExternalCommand(data);
                    }
                    else if (Type == "INTERNAL")
                    {
                        // INTERNAL commands come from one of the modules.
                        // right now, this includes:
                        // (1) Comm module recognized a disconnection from the ground 
                        // during a camera action, and requests to stop all operations.
                        string[] dataElems = data.Split(',');
                        string type = dataElems[0];
                        if (type == "Camera")
                        {
                            string cmd = dataElems[1];
                            if (cmd == "Stop")
                            {
                                // TODO change taskID to something meaningful
                                bool sqlRes = SqlHelper.AddCameraTask("1", cmd, -1, "False", "False");

                                // change the sensors module sample rate to a lower value
                                // to a normal, low value (for the HBs).
                                // currently we keep it at 1 sample per 5 seconds.
                                SqlHelper.AddSensorsTask(GetID(), "SETTINGS", regularSensorsSampleRate);

                                // TODO special Logger functions for commands, projects, etc?
                                ManagerAction("1", "Camera", "Internal", cmd, "NEW", $"Self-initiated camera stop");
                            }
                        }
                        else if (type == "Navigation")
                        {
                            string cmd = dataElems[1];
                            if (cmd == "Stop")
                            {
                                // TODO change taskID to something meaningful
                                bool sqlRes = SqlHelper.AddNavigationTask("1", cmd);

                                // TODO special Logger functions for commands, projects, etc?
                                ManagerAction("1", "Navigation", "Internal", cmd, "NEW", $"Self-initiated navigation stop");

                                sqlRes = sqlRes & SqlHelper.AddCameraTask("1", cmd, -1, "False", "False");

                                // change the sensors module sample rate to a lower value
                                // to a normal, low value (for the HBs).
                                // currently we keep it at 1 sample per 5 seconds.
                                SqlHelper.AddSensorsTask(GetID(), "SETTINGS", regularSensorsSampleRate);
                            }
                        }

                    }
                    // recieved a Calibrate-Command from Communication
                    else if (Type == "CALIBRATE")
                    {
                        string[] dataElems = data.Split(',');
                        string type = dataElems[1];
                        string cmd = dataElems[2];
                        if (type == "Driver")
                        {
                            int cmdVal = Convert.ToInt32(dataElems[3]);
                            int cmdSpeed = Convert.ToInt32(dataElems[4]);
                            int cmdTime = Convert.ToInt32(dataElems[5]);
                            int cmdQuadrant = Convert.ToInt32(dataElems[6]);
                            int cmdRadius = Convert.ToInt32(dataElems[7]);

                            // calibrate the Driver
                            bool sqlRes = SqlHelper.AddDriverTask("1", cmd, cmdVal, cmdSpeed,
                                                                  cmdTime, cmdQuadrant, cmdRadius);

                            // log action
                            ManagerAction("NA", "Driver", "Calibrate", cmd, "NEW", "");
                        } else if (type == "Crane")
                        {
                            string relevantData = string.Join(",", dataElems.Skip(3));

                            // calibrate the Crane
                            bool sqlRes = SqlHelper.AddCraneTask("1", cmd, -1, -1, -1, relevantData);

                            // log action
                            ManagerAction("NA", "Driver", "Calibrate", cmd, "NEW", "");

                        }
                    }
                    // recieved a DB copy request from Communication
                    else if (Type == "REQUEST_DB")
                    {
                        // DB requests are performed immediatly
                        Task.Factory.StartNew(() =>
                        {
                            // export the DB
                            ProcessStartInfo startInfo = new ProcessStartInfo() { FileName = "/bin/bash", Arguments = robinFolder + @"/Scripts/mysqldump.sh" };
                            Process proc = new Process() { StartInfo = startInfo, };
                            proc.Start();
                            proc.WaitForExit();

                            // notify Communication to send the db to ground
                            SqlHelper.AddRequestDbDone(1, robinFolder + @"/DB/RobinDB.sql");

                            // log action
                            ManagerAction("NA", "Manager", "Request", "DB_Copy", "DONE", "");
                        });

                        // TODO : Does manager need feedback that this WAS sent? 
                        // TODO : do I need to check that Im still connected to ground?
                    }
                    // recieved a DB clean request from Communication
                    else if (Type == "REQUEST_CLEAN")
                    {
                        // put in a DB clean order for Maintenance module
                        // WARNING: maintenance WILL violently restart the robot
                        // disregarding any ongoing task
                        SqlHelper.AddMaintenanceDbClean();
                        // log action
                        ManagerAction("NA", "Maintenance", "Request", "DB_Clean", "DONE", "Passed to Maintanence to perform");
                    }
                    else if (Type == "SOFT_RESET")
                    {
                        // stop the current project, if one exists
                        StopCurrenProject();

                        // notify Communication that you've stopped any running project
                        SqlHelper.AddSoftResetDone(1);

                        // log action
                        ManagerAction("NA", "Manager", "Request", "Soft_Reset", "DONE", "");

                    }
                    // recieved a Settings change for some module from Communication
                    else if (Type == "SETTINGS")
                    {
                        // get the different settings
                        string[] dataElems = data.Split(',');
                        if (dataElems.Length < 3)
                        {
                            Error("CheckAllModules", $"Recieved a settings string with a bad format: [{data}]");
                            continue;
                        }
                        string RequestID = dataElems[1];
                        string module = dataElems[2];

                        switch (module)
                        {
                            case "Sensors":
                                if (dataElems.Length != 4)
                                {
                                    Error("CheckAllModules", $"Recieved a settings string with a bad format for a \"Sensors\" settings: [{data}]");
                                    continue;
                                }
                                // sensorsData sampling rate
                                double sensorsDataRate = Convert.ToDouble(dataElems[3]);
                                if (SqlHelper.AddSensorsTask(RequestID, "SETTINGS", sensorsDataRate))
                                {
                                    GroundSettingsTaskID = RequestID;
                                }
                                break;
                            case "Crane":
                                if (dataElems.Length != 11)
                                {
                                    Error("CheckAllModules", $"Recieved a settings string with a bad format for a \"Crane\" settings: [{data}]");
                                    continue;
                                }
                                string relevantData = string.Join(",", dataElems.Skip(3));
                                if (SqlHelper.AddCraneTask(RequestID, "SETTINGS", -1, -1, -1, relevantData))
                                {
                                    GroundSettingsTaskID = RequestID;
                                }
                                break;
                            default:
                                break;
                        }

                        // log action
                        ManagerAction("NA", module, "Request", "Settings", "NEW", "");
                    }
                    // recieved a SHUTDOWN command from Communication
                    else if (Type == "SHUTDOWN")
                    {
                        // This command is receieved from a groundstation.
                        // shutdown should be done carefully to make sure we
                        // close carefully while saveing anything that needs saving
                        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                        {
                            // TODO we need to msg all the different modules and stop them.
                            // (or not? are we assuming this is the user's responsibility?)

                            // log action
                            ManagerAction("NA", "Manager", "Request", "Shutdown", "DONE", "");

                            ProcessStartInfo startInfo = new ProcessStartInfo() { FileName = "/bin/bash", Arguments = robinFolder + @"/Scripts/shutdown.sh", };
                            Process proc = new Process() { StartInfo = startInfo, };
                            proc.Start();
                        }
                    }
                }

                // update the IncomingMsgs with the processed IDs and update ModuleJobs to indicate you read the new msgs                                
                bool sqlRes4 = SqlHelper.UpdateCommunicationInChecked(processedIDs);
                if (sqlRes4 == false) return false; //TODO continue? break? debate this later.
            }

            return true;
        }

        /// <summary>
        /// check for any changes in the Driver tables.
        /// uses a string made of 0s and 1s that denotes changes to the module's tables.
        /// </summary>
        /// <param name="BinaryModuleStatus">the binary string</param>
        /// <returns></returns>
        private bool CheckDriver(string BinaryModuleStatus)
        {
            if (BinaryModuleStatus[0] == '1')
            {
                // check the current running task for updates

                if (currentProject != null && currentMission != null && currentTask != null)
                {
                    string TaskID = GetTaskID(currentProject.ProjectID, currentMission.MissionStep, currentTask.TaskStep);
                    string TaskStatus = SqlHelper.GetModuleTaskStatus("driver", TaskID);
                    if (TaskStatus == null) return false; //TODO continue or break? debate this later.

                    if (!Enum.TryParse(TaskStatus, out ProjectStatus status)) return false;
                    switch (status)
                    {
                        case ProjectStatus.NEW:
                            // TODO this is a new task. why is there flag up?
                            break;
                        case ProjectStatus.RUNNING:
                            // TODO this is a running task. why is there flag up?
                            break;
                        case ProjectStatus.ERROR:
                            // TODO handle the error somehow
                            currentTask.Status = ProjectStatus.ERROR;
                            break;
                        case ProjectStatus.CANCELED:
                            // TODO who canceled this? not us?
                            break;
                        case ProjectStatus.DONE:
                            lock (projChangeLock)
                            {
                                // log action
                                DriverTask dTask = (DriverTask)currentTask;
                                string ActionType = "Task";
                                if (currentProject != null)
                                {
                                    if (currentProject.Type == ProjectType.GROUNDSTATION_EXTERNAL)
                                    {
                                        ActionType = "External";
                                    }

                                    else if (currentProject.Type == ProjectType.INTERNAL_REQUESTS)
                                    {
                                        ActionType = "InternalRequest";
                                        SqlHelper.UpdateRequestStatus("navigation", TaskID, "DONE");
                                    }
                                }
                                ManagerAction(TaskID, "Driver", ActionType, dTask.driverType.ToString(), "DONE", "");

                                Tracking("CheckAllModules", $"Task #{currentTask.TaskStep} DONE.");
                                currentTask.Status = ProjectStatus.DONE;
                                currentTask = null;
                            }
                            break;
                        default:
                            break;
                    }
                }
            }

            return true;
        }

        /// <summary>
        /// check for any changes in the Crane tables.
        /// uses a string made of 0s and 1s that denotes changes to the module's tables.
        /// </summary>
        /// <param name="BinaryModuleStatus">the binary string</param>
        /// <returns></returns>
        private bool CheckCrane(string BinaryModuleStatus)
        {
            if (BinaryModuleStatus[0] == '1')
            {
                // did crane finish a settings update?
                if (GroundSettingsTaskID != null)
                {
                    string status = SqlHelper.GetModuleTaskStatus("Crane", GroundSettingsTaskID);
                    if (status != null && status == "DONE")
                    {

                        SqlHelper.AddSettingsChanged(1, "Crane");
                        GroundSettingsTaskID = null;

                        // log action
                        ManagerAction("NA", "Crane", "Request", "Settings", "DONE", "");
                    }
                }
                // check the current running task for updates
                else if (currentProject != null && currentMission != null && currentTask != null)
                {
                    string TaskID = GetTaskID(currentProject.ProjectID, currentMission.MissionStep, currentTask.TaskStep);
                    string TaskStatus = SqlHelper.GetModuleTaskStatus("Crane", TaskID);
                    if (TaskStatus == null) return false; //TODO continue or break? debate this later.

                    CraneTask cTask = (CraneTask)currentTask;
                    string ActionType = "Task";
                    ProjectStatus status = (ProjectStatus)Enum.Parse(typeof(ProjectStatus), TaskStatus);
                    // TODO try-catch this parse. can this fail? or does it go to default?
                    switch (status)
                    {
                        case ProjectStatus.RUNNING:
                        case ProjectStatus.NEW:
                            // TODO this isnt a done task. why is there flag up?
                            break;
                        case ProjectStatus.ERROR:
                        case ProjectStatus.ERROR_CRANE_0:
                        case ProjectStatus.ERROR_CRANE_1:
                            // TODO handle the error somehow (repeat the task? something else?)
                            ManagerAction(TaskID, "Crane", ActionType, cTask.craneType.ToString(),
                                          status.ToString(), GetProjectStatusDetails(status));

                            Tracking("CheckAllModules", $"Task #{currentTask.TaskStep} DONE with status={status}.");
                            currentTask.Status = status;
                            currentTask = null;
                            break;
                        case ProjectStatus.CANCELED:
                            // TODO who canceled this? not us?
                            break;
                        case ProjectStatus.DONE:
                            lock (projChangeLock)
                            {
                                // log action
                                if (currentProject != null)
                                {
                                    if (currentProject.Type == ProjectType.GROUNDSTATION_EXTERNAL)
                                    {
                                        ActionType = "External";
                                    }
                                }
                                ManagerAction(TaskID, "Crane", ActionType, cTask.craneType.ToString(),
                                              status.ToString(), GetProjectStatusDetails(status));

                                Tracking("CheckAllModules", $"Task #{currentTask.TaskStep} DONE.");
                                currentTask.Status = ProjectStatus.DONE;
                                currentTask = null;
                            }
                            break;
                        default:
                            break;
                    }
                }
            }

            return true;
        }

        /// <summary>
        /// check for any changes in the Sensors tables.
        /// uses a string made of 0s and 1s that denotes changes to the module's tables.
        /// </summary>
        /// <param name="BinaryModuleStatus">the binary string</param>
        /// <returns></returns>
        private bool CheckSensors(string BinaryModuleStatus)
        {
            if (BinaryModuleStatus[0] == '1')
            {
                // did sensors finish a settings update?
                if (GroundSettingsTaskID != null)
                {
                    string status = SqlHelper.GetModuleTaskStatus("Sensors", GroundSettingsTaskID);
                    if (status != null && status == "DONE")
                    {

                        SqlHelper.AddSettingsChanged(1, "Sensors");
                        GroundSettingsTaskID = null;

                        // log action
                        ManagerAction("NA", "Sensors", "Request", "Settings", "DONE", "");
                    }
                }
            }

            return true;
        }

        /// <summary>
        /// check for any changes in the Camera tables.
        /// uses a string made of 0s and 1s that denotes changes to the module's tables.
        /// </summary>
        /// <param name="BinaryModuleStatus">the binary string</param>
        /// <returns></returns>
        private bool CheckCamera(string BinaryModuleStatus)
        {
            if (BinaryModuleStatus[0] == '1')
            {
                if (CurrentCameraTaskID != null)
                {
                    string status = SqlHelper.GetModuleTaskStatus("Camera", CurrentCameraTaskID);
                    if (status != null && status == "DONE")
                    {
                        // tell communication that it can stop trying to send images
                        SqlHelper.AddCameraTaskDone();
                        CurrentCameraTaskID = null;
                        // log action
                        ManagerAction("NA", "Camera", "Manual", "Capture", "DONE", "");
                    }
                }
            }

            return true;
        }

        /// <summary>
        /// check for any changes in the Navigation tables.
        /// uses a string made of 0s and 1s that denotes changes to the module's tables.
        /// </summary>
        /// <param name="BinaryModuleStatus">the binary string</param>
        /// <returns></returns>
        private bool CheckNavigation(string BinaryModuleStatus)
        {
            // Navigation is currently running
            if (CurrentNavigationTaskID != null)
            {
                // Update in NavigationTasks table
                if (BinaryModuleStatus[0] == '1')
                {
                    string status = SqlHelper.GetModuleTaskStatus("navigation", CurrentNavigationTaskID);
                    if (status != null && status == "DONE")
                    {
                        CurrentNavigationTaskID = null;
                        // log action
                        ManagerAction("NA", "Navigation", "Manual", "Navigate", "DONE", "");

                        return true;
                    }
                }

                // Update in NavigationRequests table
                if (BinaryModuleStatus[1] == '1')
                {
                    // Get all new msgs from NavigationRequests table
                    List<string> msgData = SqlHelper.GetModuleRequestsByStatus("navigation", "NEW");
                    if (msgData == null) return false; //TODO continue or break? decide.

                    // Iterate new msgs and perform actions
                    foreach (string msgLine in msgData)
                    {
                        string[] msgElem = msgLine.Split('|');
                        string TaskID = msgElem[1];
                        string data = msgElem[2];
                        ParseRequest("navigation", TaskID, data);
                    }
                }
            }

            return true;
        }

        /// <summary>
        /// a special function that helps us write more detailed logs in the "ManagerActions"
        /// table. 
        /// </summary>
        /// <param name="status">the status</param>
        /// <returns>a msg string</returns>
        private string GetProjectStatusDetails(ProjectStatus status)
        {
            switch (status)
            {
                case ProjectStatus.NEW:
                    return "New task";
                case ProjectStatus.RUNNING:
                    return "Running task";
                case ProjectStatus.ERROR:
                    return "This task finished with an unspecified error";
                case ProjectStatus.ERROR_CRANE_0:
                    return "Crane task finished without being able to verify end status";
                case ProjectStatus.ERROR_CRANE_1:
                    return "Crane task finished outside the specified parameters";
                case ProjectStatus.CANCELED:
                    return "Canceled task";
                case ProjectStatus.DONE:
                    return "Finished task";
                default:
                    return "Unknown Task status";
            }
        }

        /// <summary>
        /// Used by the "CheckCommunication" function to parse the incoming 
        /// external messages as sent by the ground.
        /// </summary>
        /// <param name="data">the data to parse</param>
        private void parseExternalCommand(string data)
        {
            // first, parse data and create a task from it
            string[] dataElems = data.Split(',');
            int endStep = Convert.ToInt32(dataElems[0]);
            int taskStep = Convert.ToInt32(dataElems[1]);
            string ProjectID = dataElems[2];
            string type = dataElems[3];
            string cmd = dataElems[4];
            int value = Convert.ToInt32(dataElems[5]);
            MissionTask extTask;
            if (type == "Driver")
            {
                if (!Enum.TryParse(cmd, out DriverTypes dType))
                {
                    Error("parseExternalCommand", $"Couldn't parse Driver Cmd Type. check string: {data}");
                    return;
                }
                if (dType == DriverTypes.Curved)
                {
                    extTask = new DriverTask(dType, value, Convert.ToInt32(dataElems[6]), Convert.ToInt32(dataElems[7]),
                                             Convert.ToInt32(dataElems[8]), taskStep);
                }
                else
                {
                    extTask = new DriverTask(dType, value, -1, taskStep);
                }
            } else if (type == "Crane")
            {
                if (!Enum.TryParse(cmd, out CraneTypes cType))
                {
                    Error("parseExternalCommand", $"Couldn't parse Crane Cmd Type. check string: {data}");
                    return;
                }

                extTask = new CraneTask(cType, value, taskStep);
            }
            else
            {
                Error("parseExternalCommand", $"Couldn't parse external data. check string: {data}");
                return;
            }

            //second, add the task to an existing or new external project
            if (currentProject != null && currentProject.ProjectID == ProjectID)
            {
                // same external project. add the data as a task to it.
                if (currentProject.Missions.Count == 1)
                {
                    currentProject.Missions[0].AddTask(extTask);
                }
                else
                {
                    // TODO this shouldnt happen
                }
            }
            else
            {
                // the external project is new.
                // stop the current project, if one exists
                StopCurrenProject();

                // start a new one with a single mission
                // TODO robin name and ground name                
                Mission extMission = new Mission(1, "External Mission", new List<MissionTask>());
                extMission.AddTask(extTask);
                currentProject = new Project(ProjectID, ProjectType.GROUNDSTATION_EXTERNAL, -1,
                    "", "", new List<Mission>() { extMission });
            }
            if (extTask.TaskStep == endStep)
            {
                // if this is the last expected task in the packet, signal it
                SqlHelper.AddExternalPacketDone(1);
            }
            string TaskID = GetTaskID(currentProject.ProjectID, 1, taskStep);
            ManagerAction(TaskID, type, "External", cmd, "NEW", $"Dist:{value}");
        }

        /// <summary>
        /// Used by the "CheckNavigation" function to parse the incoming 
        /// internal messages (requests) as sent by the Navigation module.
        /// </summary>
        /// <param name="data">the data to parse</param>
        private void ParseRequest(string module, string TaskID, string data)
        {
            // Parse data and create a task
            string[] TaskIDElems = TaskID.Split('.');
            string[] dataElems = data.Split(',');
            string ProjectID = TaskIDElems[0];
            int taskStep = Convert.ToInt32(TaskIDElems[2]);
            string type = dataElems[0];
            string cmd = dataElems[1];
            int value = Convert.ToInt32(dataElems[2]);
            MissionTask reqTask;
            if (type == "Driver")
            {
                if (!Enum.TryParse(cmd, out DriverTypes dType))
                {
                    Error("ParseRequest", $"Couldn't parse Driver Cmd Type. check string: {data}");
                    return;
                }
                if (dType == DriverTypes.Curved)
                {
                    reqTask = new DriverTask(dType, value, Convert.ToInt32(dataElems[3]), Convert.ToInt32(dataElems[4]),
                                             Convert.ToInt32(dataElems[5]), taskStep);
                }
                else
                {
                    reqTask = new DriverTask(dType, value, -1, taskStep);
                }
            }

            else
            {
                Error("ParseRequest", $"Couldn't parse request data. check string: {data}");
                return;
            }

            // Add the task to an existing or new internal request project
            if (currentProject != null && currentProject.ProjectID == ProjectID)
            {
                // same internal request project. add the data as a task to it.
                if (currentProject.Missions.Count == 1)
                {
                    currentProject.Missions[0].AddTask(reqTask);
                    if (type == "Driver")
                    {
                        currentProject.Missions[0].AddTask(new DriverTask(DriverTypes.Forward, -1, -1, taskStep + 1)); // Infinity forward
                    }
                }
                else
                {
                    // TODO this shouldnt happen
                }
            }
            else
            {
                // the internal request project is new.
                // stop the current project, if one exists
                StopCurrenProject();

                // Start a new project
                // TODO robin name and ground name                
                Mission reqMission = new Mission(1, "Internal_Request_Mission", new List<MissionTask>());
                reqMission.AddTask(reqTask);
                if (type == "Driver")
                {
                    reqMission.AddTask(new DriverTask(DriverTypes.Forward, -1, -1, taskStep + 1)); // Infinity forward
                }

                currentProject = new Project(ProjectID, ProjectType.INTERNAL_REQUESTS, -1,
                    "", "", new List<Mission>() { reqMission });
                currentMission = null;
                currentTask = null;
            }
            ManagerAction(TaskID, type, "Internal Request", cmd, "NEW", $"Dist:{value}");

            // Update request status to RUNNING
            SqlHelper.UpdateRequestStatus(module, TaskID, "RUNNING");

            if (type == "Driver")
            {
                string forwardTaskID = GetTaskID(currentProject.ProjectID, 1, taskStep + 1);
                ManagerAction(forwardTaskID, type, "Internal Request", "Forward", "NEW", $"Dist:infinity");
            }
        }

        /// <summary>
        /// main function to analyze and run the current project
        /// </summary>
        private void ProjectHandler()
        {
            lock (projChangeLock)
            {
                // is there a current project?
                if (currentProject == null) return;

                // are we running a mission/task?
                if (currentMission == null)
                {
                    //not running a mission, start one
                    if (currentTask != null)
                    {
                        // shouldn't happen. something went wrong - abort the entire project.
                        Error("ProjectHandler", "Project/Mission is null but task exists.");
                        ResetProjectVars();
                    }
                    else
                    {
                        // no mission/task started. start one.
                        StartNextTask();
                    }
                } else
                {
                    if (currentTask == null)
                    {
                        // there is a project/mission, but no task.
                        // it probably ended, select a new one.
                        StartNextTask();
                    } else
                    {
                        // special case. 
                        // (1) if we're running an endless external task, and 
                        // the next task is a "Stop" command - cancel the current
                        // task.                        
                        if (currentProject.Type == ProjectType.GROUNDSTATION_EXTERNAL &&
                            currentMission.tasks.Count > CurrentTaskInd + 1)
                        {
                            // this is an external project, and there is another task.
                            MissionTask nextTask = currentMission.tasks[CurrentTaskInd + 1];
                            // is the next task a Stop?
                            if (nextTask.Type == MissionTaskModule.Driver &&
                                ((DriverTask)nextTask).driverType == DriverTypes.Stop)
                            {
                                // the next task IS a driver-stop, so tell Driver to stop
                                StopCurrentTask();
                            }
                        }
                    }
                }
            }
        }

        /// <summary>
        /// Stop a running task, for any reason.
        /// NOTICE: if the mission/project has more tasks, this will cause it 
        /// to continue to the next one. If you want to cancel the WHOLE
        /// project, use StopCurrentProject.
        /// * currently used for EXTERNAL type pojects.
        /// </summary>
        private void StopCurrentTask()
        {
            if (currentProject == null || currentMission == null || currentTask == null)
            {
                Error("StopCurrentTask", "Can't cancel the task, mission some of the project vars");
                return;
            }
            lock (projChangeLock)
            {
                if (currentTask != null)
                {
                    // cancel the running task and update the module (update ModuleJobs and Tasks SQL tables)                
                    bool sqlRes = SqlHelper.CancelModuleTasks(currentTask.Type);
                    if (sqlRes == false) return; //TODO return or retry? debate this later.

                    // log action
                    if (currentTask.Type == MissionTaskModule.Driver)
                    {
                        DriverTask dTask = (DriverTask)currentTask;
                        string TaskID = GetTaskID(currentProject.ProjectID, currentMission.MissionStep, dTask.TaskStep);
                        ManagerAction(TaskID, "Driver", "Task", dTask.driverType.ToString(), "CANCELED", "");
                    }
                    currentTask.Status = ProjectStatus.CANCELED;

                    // update the project file                
                    bool result = XmlHelper.CreateXmlFromProject(currentProject, projectBase);
                    if (result == false)
                    {
                        // TODO what should I do here? cant return bc I still wish to null the curr
                    }

                    CurrentTaskInd++;
                    currentTask = null;
                }
            }
        }

        /// <summary>
        /// Stop the current running task (due to an outside command, error or something else).
        /// this also cancels the entire project.
        /// </summary>
        private void StopCurrenProject()
        {
            lock (projChangeLock)
            {
                if (currentProject != null)
                {
                    MissionTask task = currentProject.Missions[CurrentMissionInd].tasks[CurrentTaskInd];

                    // cancel the running task and update the module (update ModuleJobs and Tasks SQL tables)                
                    bool sqlRes = SqlHelper.CancelModuleTasks(task.Type);
                    if (sqlRes == false) return; //TODO return or retry? debate this later.

                    // update the project statuses
                    foreach (Mission _mission in currentProject.Missions)
                    {
                        if (_mission.Status != ProjectStatus.DONE)
                        {
                            foreach (MissionTask _task in _mission.tasks)
                            {
                                if (_task.Status != ProjectStatus.DONE)
                                {
                                    _task.Status = ProjectStatus.CANCELED;

                                    // log action
                                    if (_task.Type == MissionTaskModule.Driver)
                                    {
                                        DriverTask dTask = (DriverTask)_task;
                                        string TaskID = GetTaskID(currentProject.ProjectID, _mission.MissionStep, dTask.TaskStep);
                                        ManagerAction(TaskID, "Driver", "Task", dTask.driverType.ToString(), "CANCELED", "");
                                    }
                                    else if (_task.Type == MissionTaskModule.Crane)
                                    {
                                        CraneTask cTask = (CraneTask)_task;
                                        string TaskID = GetTaskID(currentProject.ProjectID, _mission.MissionStep, cTask.TaskStep);
                                        ManagerAction(TaskID, "Driver", "Task", cTask.craneType.ToString(), "CANCELED", "");
                                    }
                                }
                            }
                            _mission.Status = ProjectStatus.CANCELED;

                            // log action
                            ManagerAction($"{currentProject.ProjectID}.{_mission.MissionStep}", "NA", "Mission", _mission.name, "CANCELED", "");
                        }
                    }

                    // update the project file                
                    bool result = XmlHelper.CreateXmlFromProject(currentProject, projectBase);
                    if (result == false)
                    {
                        // TODO what should I do here? cant return bc I still wish to null the curr
                    }

                    // log action
                    ManagerAction($"{currentProject.ProjectID}", "NA", "Project", currentProject.Type.ToString(), "CANCELED", "");

                    Tracking("StopCurrenProject", $"Stopped current project, ID={currentProject.ProjectID}");
                    ResetProjectVars();
                }
            }
        }

        /// <summary>
        /// start next task: 1st task or the next task in the list
        /// </summary>
        private void StartNextTask()
        {
            if (currentProject == null) return;

            Mission _mission = currentProject.Missions[CurrentMissionInd];
            // find out the new mission/task IDs
            if (_mission.tasks.Count > CurrentTaskInd + 1)
            {
                // we still have tasks left in the mission
                CurrentTaskInd++;
            } else
            {
                // no tasks left in the mission
                if ((currentProject.Type != ProjectType.GROUNDSTATION_EXTERNAL) && (currentProject.Type != ProjectType.INTERNAL_REQUESTS))
                {
                    // log action
                    string MissionID = $"{currentProject.ProjectID}.{currentMission.MissionStep}";
                    ManagerAction(MissionID, "NA", "Mission", currentMission.name, "DONE", "");
                    Tracking("StartNextTask", $"Finished mission named '{currentMission.name}'");
                }
                else
                {
                    return;
                    // ignore ongoing External projects. 
                    // they always have 1 mission with added tasks.
                }

                if (currentProject.Missions.Count > CurrentMissionInd + 1)
                {
                    // we still have missions left in the project
                    CurrentTaskInd = 0;
                    CurrentMissionInd++;
                    currentMission.Status = ProjectStatus.DONE;
                }
                else
                {
                    // project finished!

                    // update the project file
                    bool result1 = XmlHelper.CreateXmlFromProject(currentProject, projectBase);
                    if (result1 == false)
                    {
                        // TODO what should be done here?
                    }

                    // log action
                    ManagerAction(currentProject.ProjectID, "NA", "Project", currentProject.Type.ToString(), "DONE", "");
                    Tracking("StartNextTask", $"Finished project, ID='{currentProject.ProjectID}'");

                    ResetProjectVars();
                    return;
                }
            }
            currentMission = currentProject.Missions[CurrentMissionInd];
            currentTask = currentMission.tasks[CurrentTaskInd];

            // update the project file
            bool result2 = XmlHelper.CreateXmlFromProject(currentProject, projectBase);
            if (result2 == false)
            {
                // TODO what should be done here?
            }

            // send the task to the appropriate module
            MissionTaskModule module = currentTask.Type;
            string TaskID = GetTaskID(currentProject.ProjectID, currentMission.MissionStep, currentTask.TaskStep);
            switch (module)
            {
                case MissionTaskModule.Driver:
                    // start a driver task, with certain SVT values
                    DriverTask nextDriverTask = (DriverTask)currentTask;
                    if (usedTaskIDs.Contains(TaskID))
                    {
                        Tracking("StartNextTask", $"Couldnt start task #{currentTask.TaskStep} in the mission: '{_mission.name}' as its TaskID={TaskID} isn't unique.");
                        ManagerAction(TaskID, "Driver", "Task", nextDriverTask.driverType.ToString(), "CANCELED", "");
                        return;
                    }

                    // add the TaskID to a set of all unique TaskIDs.
                    usedTaskIDs.Add(TaskID);

                    // insert the task to SQL
                    if (nextDriverTask.Dist < 0)
                    {
                        // movements with (dist < 0) are infinite.
                        // don't wait for Driver status update, it won't come.
                        currentTask = null;
                    }
                    bool sqlRes = SqlHelper.AddDriverTask(TaskID, nextDriverTask.driverType.ToString(),
                        nextDriverTask.Dist, nextDriverTask.Speed, nextDriverTask.Time,
                        nextDriverTask.Quadrant, nextDriverTask.Radius);
                    if (sqlRes == false) return; //TODO return or retry? debate this later.
                    break;
                case MissionTaskModule.Crane:
                    // start a crane task, currently not supporting speed/time (is it needed?)
                    CraneTask nextCraneTask = (CraneTask)currentTask;
                    // is the TaskID not unique?
                    if (usedTaskIDs.Contains(TaskID))
                    {
                        // it isnt, cancel the task/
                        Tracking("StartNextTask", $"Couldnt start task #{currentTask.TaskStep} in the mission: '{_mission.name}' as its TaskID isn't unique.");
                        ManagerAction(TaskID, "Crane", "Task", nextCraneTask.craneType.ToString(), "CANCELED", "");
                        return;
                    }

                    // add the TaskID to a set of all unique TaskIDs.
                    usedTaskIDs.Add(TaskID);

                    // insert the task to SQL
                    sqlRes = SqlHelper.AddCraneTask(TaskID, nextCraneTask.craneType.ToString(),
                        nextCraneTask.Value, -1, -1);
                    if (sqlRes == false) return; //TODO return or retry? debate this later.
                    break;
                default:
                    break;
            }
        }

        /// <summary>
        /// start a manual command (special action that is defined as having an immediate priority)
        /// </summary>
        private void StartManual()
        {
            if (currentManual == string.Empty) return;

            // parse the command
            string[] dataElem = currentManual.Split(',');
            string cmdType = dataElem[2];
            string cmd = dataElem[3];

            // commands are always a single-mission/single-task thing                
            string TaskID = GetTaskID(dataElem[1], 1, 1);

            if (cmd == "Stop")
            {
                // restore normal ModuleJobs check rate
                delayMS = 500;
            }
            else
            {
                // increase ModuleJobs check rate to catch commands quickly
                delayMS = 100;
            }

            if (cmdType == "Driver")
            {
                // TODO add stop command to Driver and update ModuleJobs

                int cmdValue = Convert.ToInt32(dataElem[4]);
                int cmdSpeed = Convert.ToInt32(dataElem[5]);
                int cmdTime = Convert.ToInt32(dataElem[6]);
                int cmdQuadrant = Convert.ToInt32(dataElem[7]);
                int cmdRadius = Convert.ToInt32(dataElem[8]);

                //  add driver task based on the command
                bool sqlRes = SqlHelper.AddDriverTask(TaskID, cmd, cmdValue, cmdSpeed, cmdTime, cmdQuadrant, cmdRadius);
                if (sqlRes == false) return; //TODO return or retry? debate this later.

                // TODO special Logger functions for commands, projects, etc?
                ManagerAction(TaskID, "Driver", "Manual", cmd, "NEW", $"(S:{cmdValue}, V:{cmdSpeed}, T:{cmdTime}");
                Tracking("StartManual", $"Started Manual Command: Module: Driver, Cmd: {cmd}, Val: {cmdValue}, Speed: {cmdSpeed}");
            }
            else if (cmdType == "Crane")
            {
                int cmdValue = Convert.ToInt32(dataElem[4]);
                int cmdSpeed = Convert.ToInt32(dataElem[5]);
                int cmdTime = Convert.ToInt32(dataElem[6]);

                //  add driver task based on the command
                bool sqlRes = SqlHelper.AddCraneTask(TaskID, cmd, cmdValue, cmdSpeed, cmdTime);
                if (sqlRes == false) return; //TODO return or retry? debate this later.

                // TODO special Logger functions for commands, projects, etc?
                ManagerAction(TaskID, "Crane", "Manual", cmd, "NEW", $"Value:{cmdValue}");
                Tracking("StartManual", $"Started Manual Command: Module: Crane, Cmd: {cmd}, Val: {cmdValue}");
            }
            else if (cmdType == "Camera")
            {
                int cmdValue = Convert.ToInt32(dataElem[4]);
                string saveLocally = dataElem[5];
                string resolution = dataElem[6];
                // add camera task
                if (cmd == "Start")
                {
                    // delete the images folder content, the user doesnt need them.
                    // This is nessecary to make sure Camera module operates properly
                    // (this cleanup can clean leftovers of a "crash", for example).
                    foreach (string dir in Directory.GetDirectories(robinFolder + "/Images/"))
                    {
                        Directory.Delete(dir, true);
                    }
                    foreach (string file in Directory.GetFiles(robinFolder + "/Images/"))
                    {
                        File.Delete(file);
                    }

                    CurrentCameraTaskID = TaskID;

                    // change the senors module sample rate to a higher value
                    // to enable good sensors data for all camera frames.
                    // currently we keep it at 6 samples per second.
                    SqlHelper.AddSensorsTask(GetID(), "SETTINGS", cameraCaptureSensorsSampleRate);
                } else if (cmd == "Stop")
                {
                    // change the sensors module sample rate to a lower value
                    // to a normal, low value (for the HBs).
                    // currently we keep it at 1 sample per 5 seconds.
                    SqlHelper.AddSensorsTask(GetID(), "SETTINGS", regularSensorsSampleRate);
                }
                bool sqlRes = SqlHelper.AddCameraTask(TaskID, cmd, cmdValue, saveLocally, resolution);
                if (sqlRes == false) return; //TODO return or retry? debate this later.

                // TODO special Logger functions for commands, projects, etc?
                ManagerAction(TaskID, "Camera", "Manual", cmd, "NEW", $"Value:{cmdValue}");
                Tracking("StartManual", $"Started Manual Command: Module: Camera, Cmd: {cmd}, Val: {cmdValue}");
            }
            else if (cmdType == "Navigation")
            {
                // add navigation task
                if (cmd == "Start")
                {
                    CurrentNavigationTaskID = TaskID;
                }
                if (cmd == "Stop")
                {
                    // TODO: STOP CAMERA, WAIT FOR ALL REQUSTS TO BE DONE SHOVAL
                    StopCurrenProject(); // Stop navigation requests to driver
                }
                bool sqlRes = SqlHelper.AddNavigationTask(TaskID, cmd);
                if (sqlRes == false) return; //TODO return or retry? debate this later.

                // TODO special Logger functions for commands, projects, etc?
                ManagerAction(TaskID, "Navigation", "Manual", cmd, "NEW", "");
                Tracking("StartManual", $"Started Manual Command: Module: Navigation, Cmd: {cmd}");
            }
        }

        /// <summary>
        /// get the special "taskID" from the task parameters
        /// </summary>
        /// <param name="projectID">Project ID</param>
        /// <param name="missionStep">Step in the project</param>
        /// <param name="TaskStep">Step in the mission</param>
        /// <returns></returns>
        private string GetTaskID(string projectID, int missionStep, int TaskStep)
        {
            return $"{projectID}.{missionStep}.{TaskStep}";
        }

        /// <summary>
        /// add a special manager action to the ManagerActions table, a table 
        /// that logs all actions done by the manager.
        /// </summary>
        /// <param name="ActionID">action ID</param>
        /// <param name="Module">relevant module</param>
        /// <param name="ActionType">action main-type</param>
        /// <param name="Action">action sub-type</param>
        /// <param name="Status">action status</param>
        /// <param name="Details">more details</param>
        private void ManagerAction(string ActionID, string Module, string ActionType, string Action, string Status, string Details)
        {
            SqlHelper.AddManagerAction(ActionID, Module, ActionType, Action, Status, Details);
        }

        /// <summary>
        /// create manager action logs from a new incoming project
        /// </summary>
        /// <param name="project">project object</param>
        private void ManagerActionTypeProject(Project project)
        {
            ManagerAction(project.ProjectID, "NA", "Project", project.Type.ToString(), "NEW", "");

            foreach (Mission mission in project.Missions)
            {
                string MissionID = $"{project.ProjectID}.{mission.MissionStep}";
                ManagerAction(MissionID, "NA", "Mission", mission.name, "NEW", "");

                foreach (MissionTask task in mission.tasks)
                {
                    switch (task.Type)
                    {
                        case MissionTaskModule.Driver:
                            DriverTask dTask = (DriverTask)task;
                            string TaskID = GetTaskID(project.ProjectID, mission.MissionStep, dTask.TaskStep);
                            ManagerAction(TaskID, "Driver", "Task", dTask.driverType.ToString(), "NEW", $"Distance:{dTask.Dist}");
                            break;
                        case MissionTaskModule.Crane:
                            CraneTask cTask = (CraneTask)task;
                            TaskID = GetTaskID(project.ProjectID, mission.MissionStep, cTask.TaskStep);
                            ManagerAction(TaskID, "Crane", "Task", cTask.craneType.ToString(), "NEW", $"Angle:{cTask.Value}");
                            break;
                        default:
                            break;
                    }
                }
            }
        }

        /// <summary>
        /// reset all current project/mission/task related variables
        /// </summary>
        private void ResetProjectVars()
        {
            CurrentMissionInd = 0;
            CurrentTaskInd = -1; // CurrentTaskInd starts at (-1) to be able to start the first task
            currentProject = null;
            currentMission = null;
            currentTask = null;
        }

        #endregion

        #region Misc functions

        /// <summary>
        /// Initialize the port connection to the Sabertooth controller (via a special library)
        /// IS THIS STILL RELEVANT??
        /// </summary>
        /// <returns></returns>
        public bool InitializeSabertooth()
        {
            try
            {
                if (btnConnectSaber.Text == string.Empty || txtSaberBaud.Text == string.Empty)
                {
                    rtxtMsgs.Text = "Please choose the Comport name and Baud-Rate.";
                    return false;
                }
                //saber = new Sabertooth(comboSaberPorts.Text, Convert.ToInt32(txtSaberBaud.Text)); // which port is it connected to?

                //       If you have a 2x12, 2x25 V2, 2x60 or SyRen 50, you can remove                                                          
                //       the autobaud line and save yourself two seconds of startup delay.
                //saber.AutoBaud();
            }
            catch (Exception e)
            {
                Error("InitializeSabertooth", "Couldn't start sabertooth", e);
                return false;
            }
            return true;
        }

        /// <summary>
        /// IS THIS STILL RELEVANT??
        /// </summary>
        /// <returns></returns>
        public bool InitializeLynxmotion()
        {
            try
            {
                if (comboLynxPorts.Text == string.Empty || txtLynxBaud.Text == string.Empty)
                {
                    rtxtMsgs.Text = "Please choose the Comport name and Baud-Rate.";
                    return false;
                }
                // if the port is open close it
                if (LynxPort.IsOpen) LynxPort.Close();

                LynxPort.PortName = comboLynxPorts.Text;
                LynxPort.BaudRate = Convert.ToInt32(txtLynxBaud.Text);

                LynxPort.Open();
            }
            catch (Exception e)
            {
                Error("InitializeLynxmotion", "Couldn't start Lynxmotion", e);
                return false;
            }
            return true;
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

        #endregion

        #region GUI actions

        /// <summary>
        /// IS THIS RELEVANT?
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void btnConnectSaber_Click(object sender, EventArgs e)
        {
            if (!InitializeSabertooth())
            {
                btnConnectSaber.BackColor = Color.Red;
            }
            else
            {
                btnConnectSaber.BackColor = Color.Green;
                Tracking("btnConnectSaber_Click", $"Successfully connected to Comport=\"{btnConnectSaber.Text}\"");
                rtxtMsgs.Text = "Saber connected!";
                btnConnectSaber.Text = "Connected";
                btnConnectSaber.Enabled = false;
            }
        }

        /// <summary>
        /// IS THIS RELEVANT?
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void btnConnectLynx_Click(object sender, EventArgs e)
        {
            if (!InitializeLynxmotion())
            {
                btnConnectLynx.BackColor = Color.Red;
            }
            else
            {
                btnConnectLynx.BackColor = Color.Green;
                Tracking("btnConnectSaber_Click", $"Successfully connected to Comport=\"{comboLynxPorts.Text}\"");
                rtxtMsgs.Text = "Lynx connected!";
                btnConnectLynx.Text = "Connected";
                btnConnectLynx.Enabled = false;
            }
        }

        // is this still relevant??
        private void btnForward_Click(object sender, EventArgs e)
        {
            //DriverCommandSqlActions("Forward", numDrive.Value, numDriveTime.Value);
            // TODO return the manual commands from manager
        }

        // is this still relevant??
        private void btnBackwards_Click(object sender, EventArgs e)
        {
            //DriverCommandSqlActions("Backward", numDrive.Value, numDriveTime.Value);
            // TODO return the manual commands from manager
        }

        // is this still relevant??
        private void btnLeft_Click(object sender, EventArgs e)
        {
            //DriverCommandSqlActions("Left", numTurn.Value, numDriveTime.Value);
            // TODO return the manual commands from manager
        }

        // is this still relevant??
        private void btnRight_Click(object sender, EventArgs e)
        {
            //DriverCommandSqlActions("Right", numTurn.Value, numDriveTime.Value);
            // TODO return the manual commands from manager
        }

        // is this still relevant??
        private void btnStopDriver_Click(object sender, EventArgs e)
        {
            //DriverCommandSqlActions("Stop", 0, 100);
            // TODO return the manual commands from manager
        }

        // is this still relevant??
        private void btnCraneArmUp_Click(object sender, EventArgs e)
        {
            //CraneCommandSqlActions("ArmUp", numCraneArm.Value, numCraneTime.Value);
            // TODO return the manual commands from manager
        }

        // is this still relevant??
        private void btnCraneArmDown_Click(object sender, EventArgs e)
        {
            //CraneCommandSqlActions("ArmDown", numCraneArm.Value, numCraneTime.Value);
            // TODO return the manual commands from manager
        }

        // is this still relevant??
        private void btnCraneWristUp_Click(object sender, EventArgs e)
        {
            //CraneCommandSqlActions("WristUp", numCraneWrist.Value, numCraneTime.Value);
            // TODO return the manual commands from manager
        }

        // is this still relevant??
        private void btnCraneWristDown_Click(object sender, EventArgs e)
        {
            //CraneCommandSqlActions("WristDown", numCraneWrist.Value, numCraneTime.Value);
            // TODO return the manual commands from manager
        }

        // is this still relevant??
        private void btnCraneClawOpen_Click(object sender, EventArgs e)
        {
            //CraneCommandSqlActions("ClawOpen", numCraneClaw.Value, numCraneTime.Value);
            // TODO return the manual commands from manager
        }

        // is this still relevant??
        private void btnCraneClawClose_Click(object sender, EventArgs e)
        {
            //CraneCommandSqlActions("ClawClose", numCraneClaw.Value, numCraneClaw.Value);
            // TODO return the manual commands from manager
        }

        // is this still relevant??
        private void btnCageOpen_Click(object sender, EventArgs e)
        {
            //CraneCommandSqlActions("CageOpen", numCage.Value, numCage.Value);
            // TODO return the manual commands from manager
        }

        // is this still relevant??
        private void btnCageClose_Click(object sender, EventArgs e)
        {
            //CraneCommandSqlActions("CageClose", numCage.Value, numCage.Value);
            // TODO return the manual commands from manager
        }

        #endregion
    }
}
