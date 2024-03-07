using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

//using System.Runtime.InteropServices; // tells us if we're on Windows OR Linux
using MySql.Data.MySqlClient;
using System.Data.SqlClient;

namespace ManagerGUI.Utility
{
    static class SqlHelper
    {
        static readonly string MySqlConnectionString = System.Configuration.ConfigurationManager.ConnectionStrings["RobinDB_MySQL"].ConnectionString;
        static readonly string SqlConnectionString = System.Configuration.ConfigurationManager.ConnectionStrings["RobinDB_SQL"].ConnectionString;

        public delegate void EventLog(string function, string Msg);
        public static event EventLog Tracking;
        public delegate void EventErrorLog(string function, string Msg, Exception e = null);
        public static event EventErrorLog Error;

        /// <summary>
        /// retrieves all the ModuleJob table rows to check for module updates
        /// </summary>
        /// <returns></returns>
        public static List<string> GetAllNewMsgs()
        {
            List<string> lines = new List<string>();
            string selectNewCommunications = "SELECT * " +
                                            $"FROM communicationin " +
                                             "WHERE Status='NEW'";

            try
            {
                using (MySqlConnection connection = new MySqlConnection(MySqlConnectionString))
                {
                    connection.Open();
                    using (MySqlCommand command = new MySqlCommand(selectNewCommunications, connection))
                    {
                        using (MySqlDataReader reader = (MySqlDataReader)command.ExecuteReader())
                        {
                            while (reader.Read())
                            {
                                lines.Add($"{reader[0]}|{reader[2]}|{reader[3]}|{reader[4]}");
                            }
                        }
                    }
                }
            }
            catch (SqlException sqlE)
            {
                Error("GetAllNewMsgs", "Encountered an issue with SQL", sqlE);
                return null;
                //return new ErrorValue<List<string>>(null, "Encountered an issue with SQL ", sqlE);
            }
            catch (MySqlException sqlE)
            {
                Error("GetAllNewMsgs", "Encountered an issue with SQL", sqlE);
                return null;
                //return new ErrorValue<List<string>>(null, "Encountered an issue with SQL ", sqlE);
            }
            catch (Exception e)
            {
                Error("GetAllNewMsgs", "Unknown issue", e);
                return null;
                //return new ErrorValue<List<string>>(null, "Unknown issue ", e);
            }

            return lines;
            //return new ErrorValue<List<string>>(lines);
        }

        public static List<string> GetModuleRequestsByStatus(string module, string status)
        {
            List<string> lines = new List<string>();
            string selectNewRequests = "SELECT * " +
                                      $"FROM {module}requests " +
                                      $"WHERE Status='{status}'";

            try
            {
                using (MySqlConnection connection = new MySqlConnection(MySqlConnectionString))
                {
                    connection.Open();
                    using (MySqlCommand command = new MySqlCommand(selectNewRequests, connection))
                    {
                        using (MySqlDataReader reader = (MySqlDataReader)command.ExecuteReader())
                        {
                            while (reader.Read())
                            {
                                // FIX!!!!!!!!!!!!
                                lines.Add($"{reader[0]}|{reader[1]}|{reader[2]}|{reader[3]}");
                            }
                        }
                    }
                }
            }
            catch (SqlException sqlE)
            {
                Error("GetModuleNewRequests", "Encountered an issue with SQL", sqlE);
                return null;
                //return new ErrorValue<List<string>>(null, "Encountered an issue with SQL ", sqlE);
            }
            catch (MySqlException sqlE)
            {
                Error("GetModuleNewRequests", "Encountered an issue with SQL", sqlE);
                return null;
                //return new ErrorValue<List<string>>(null, "Encountered an issue with SQL ", sqlE);
            }
            catch (Exception e)
            {
                Error("GetModuleNewRequests", "Unknown issue", e);
                return null;
                //return new ErrorValue<List<string>>(null, "Unknown issue ", e);
            }

            return lines;
            //return new ErrorValue<List<string>>(lines);
        }

        public static string GetModuleTaskStatus(string module, string TaskID)
        {
            List<string> lines = new List<string>();
            string selectTaskChanges = "SELECT Status " +
                                      $"FROM {module}tasks " +
                                      $"WHERE TaskID='{TaskID}'";

            try
            {
                using (MySqlConnection connection = new MySqlConnection(MySqlConnectionString))
                {
                    connection.Open();
                    using (MySqlCommand command = new MySqlCommand(selectTaskChanges, connection))
                    {
                        using (MySqlDataReader reader = (MySqlDataReader)command.ExecuteReader())
                        {
                            while (reader.Read())
                            {
                                lines.Add(reader.GetString(reader.GetOrdinal("Status")));
                            }
                        }
                    }
                }
            }
            catch (SqlException sqlE)
            {
                Error("GetModuleTaskStatus", "Encountered an issue with SQL", sqlE);
                return null;
                //return new ErrorValue<string>(null, "Encountered an issue with SQL ", sqlE);
            }
            catch (MySqlException sqlE)
            {
                Error("GetModuleTaskStatus", "Encountered an issue with SQL", sqlE);
                return null;
                //return new ErrorValue<string>(null, "Encountered an issue with SQL ", sqlE);
            }
            catch (Exception e)
            {
                Error("GetModuleTaskStatus", "Unknown issue", e);
                return null;
                //return new ErrorValue<string>(null, "Unknown issue ", e);
            }
            if (lines.Count > 1 || lines.Count == 0)
            {
                Error("GetModuleTaskStatus", "Didn't get a single result as expected");
                return null;
                //return new ErrorValue<string>(null, "Didn't get a single result as expected");
            }

            return lines[0];
            //return new ErrorValue<string>(lines[0]);
        }

        /// <summary>
        /// retrieves all modulejobs data, excluding 'Communication' - this is in a different (faster) thread
        /// </summary>
        /// <returns></returns>
        public static List<string> GetAllModuleData()
        {
            List<string> lines = new List<string>();
            string selectAllModuleStatus = "SELECT * " +
                                          $"FROM modulejobs ";// +
                                          //"WHERE Module != 'Communication'";

            try
            {
                using (MySqlConnection connection = new MySqlConnection(MySqlConnectionString))
                {
                    connection.Open();
                    using (MySqlCommand command = new MySqlCommand(selectAllModuleStatus, connection))
                    {
                        using (MySqlDataReader reader = (MySqlDataReader)command.ExecuteReader())
                        {
                            while (reader.Read())
                            {
                                string data = $"{reader.GetString(reader.GetOrdinal("Module"))}|" +
                                                $"{reader.GetInt32(reader.GetOrdinal("ManagerStatus"))}|" +
                                                $"{reader.GetInt32(reader.GetOrdinal("ModuleStatus"))}";
                                lines.Add(data);
                            }
                        }
                    }
                }
            }
            catch (SqlException sqlE)
            {
                Error("GetAllModuleData", "Encountered an issue with SQL", sqlE);
                return null;
                //return new ErrorValue<List<string>>(null, "Encountered an issue with SQL ", sqlE);
            }
            catch (MySqlException sqlE)
            {
                Error("GetAllModuleData", "Encountered an issue with SQL", sqlE);
                return null;
                // return new ErrorValue<List<string>>(null, "Encountered an issue with SQL ", sqlE);
            }
            catch (Exception e)
            {
                Error("GetAllModuleData", "Unknown issue", e);
                return null;
                //return new ErrorValue<List<string>>(null, "Unknown issue ", e);
            }

            return lines;
            //return new ErrorValue<List<string>>(lines);
        }

        /*
        /// <summary>
        /// get the status of the communication module in the modulejobs table. 
        /// its in a different query bc communication response needs to be MUCH quicker.
        /// </summary>
        /// <returns></returns>
        public static async Task<string> GetCommunicationData()
        {
            bool IsWindowsOS = RuntimeInformation.IsOSPlatform(OSPlatform.Windows);
            List<string> lines = new List<string>();
            string selectAllModuleStatus = "SELECT * " +
                                          $"FROM modulejobs " +
                                          "WHERE Module='Communication'";

            try
            {
                // linux uses <MySql___> and windows uses <Sql___>, this is the best way I could figure out to write this section
                if (IsWindowsOS)
                {
                    using (SqlConnection connection = new SqlConnection(SqlConnectionString))
                    {
                        connection.Open();
                        using (SqlCommand command = new SqlCommand(selectAllModuleStatus, connection))
                        {
                            using (SqlDataReader reader = await command.ExecuteReaderAsync())
                            {
                                // should be 1 line...
                                while (await reader.ReadAsync())
                                {
                                    lines.Add($"{reader[1]}|{reader[2]}|{reader[3]}|{reader[4]}");
                                }
                            }
                        }
                    }
                }
                else // linux
                {
                    using (MySqlConnection connection = new MySqlConnection(SqlConnectionString))
                    {
                        connection.Open();
                        using (MySqlCommand command = new MySqlCommand(selectAllModuleStatus, connection))
                        {
                            using (MySqlDataReader reader = (MySqlDataReader)await command.ExecuteReaderAsync())
                            {
                                // should be 1 line...
                                while (await reader.ReadAsync())
                                {
                                    lines.Add($"{reader[1]}|{reader[2]}|{reader[3]}|{reader[4]}");
                                }
                            }
                        }
                    }
                }
            }
            catch (SqlException sqlE)
            {
                string errorMsg = "GetCommunicationData: Encountered an issue with SQL: " + sqlE.Message;
                Console.WriteLine(errorMsg);
                return null;
            }
            catch (MySqlException sqlE)
            {
                string errorMsg = "GetCommunicationData: Encountered an issue with SQL: " + sqlE.Message;
                Console.WriteLine(errorMsg);
                return null;
            }
            catch (Exception e)
            {
                string errorMsg = "GetCommunicationData: Unknown issue: " + e.Message;
                Console.WriteLine(errorMsg);
                return null;
            }
            if (lines.Count != 1)
            {
                string errorMsg = "GetCommunicationData: We didn't find exactly 1 line as expected. Please check the SQL table 'communicationin'";
                Console.WriteLine(errorMsg);
                return null;
            }

            return lines[0];
        }*/

        public static bool UpdateModuleChecked(string name)
        {
            List<string> lines = new List<string>();
            //update modulejobs column ModuleStatus to 0 bc we already checked all the changed tables
            string updateModuleJobRow = "UPDATE modulejobs " +
                                          $"SET ModuleStatus=0 " +
                                          $"WHERE Module='{name}'";
            try
            {
                using (MySqlConnection connection = new MySqlConnection(MySqlConnectionString))
                {
                    connection.Open();
                    using (MySqlCommand command = new MySqlCommand(updateModuleJobRow, connection))
                    {
                        command.ExecuteNonQuery();
                    }
                }
            }
            catch (SqlException sqlE)
            {
                Error("UpdateModuleChecked", "Encountered an issue with SQL", sqlE);
                return false;
            }
            catch (MySqlException sqlE)
            {
                Error("UpdateModuleChecked", "Encountered an issue with SQL", sqlE);
                return false;
            }
            catch (Exception e)
            {
                Error("UpdateModuleChecked", "Unknown issue", e);
                return false;
                //return new ErrorValue("Unknown issue", e);
            }

            return true;
        }

        public static bool UpdateRequestStatus(string module, string TaskID, string status)
        {
            List<string> lines = new List<string>();
            string updateRequestRow = $"UPDATE {module}requests " +
                                      $"SET Status='{status}' " +
                                      $"WHERE TaskID='{TaskID}'";
            try
            {
                using (MySqlConnection connection = new MySqlConnection(MySqlConnectionString))
                {
                    connection.Open();
                    using (MySqlCommand command = new MySqlCommand(updateRequestRow, connection))
                    {
                        command.ExecuteNonQuery();
                    }
                }
            }
            catch (SqlException sqlE)
            {
                Error("UpdateRequestStatus", "Encountered an issue with SQL", sqlE);
                return false;
            }
            catch (MySqlException sqlE)
            {
                Error("UpdateRequestStatus", "Encountered an issue with SQL", sqlE);
                return false;
            }
            catch (Exception e)
            {
                Error("UpdateRequestStatus", "Unknown issue", e);
                return false;
                //return new ErrorValue("Unknown issue", e);
            }

            return true;
        }

        /// <summary>
        /// cancel the module's new or running tasks
        /// </summary>
        /// <param name="module"></param>
        public static bool CancelModuleTasks(MissionTaskModule module)
        {
            List<string> lines = new List<string>();
            //update modulejobs column ModuleStatus to 0 bc we already checked all the changed tables

            string ModuleName = string.Empty;
            switch (module)
            {
                case MissionTaskModule.Driver:
                    ModuleName = "Driver";
                    break;
                case MissionTaskModule.Crane:
                    ModuleName = "Crane";
                    break;
                default:
                    break;
            }
            string TasksTable = ModuleName + "Tasks";

            string updateModuleJobRow = "UPDATE modulejobs " +
                                       $"SET ManagerStatus=1 " +
                                       $"WHERE Module='{ModuleName}'";
            string updateTasksRows =  $"UPDATE {TasksTable} " +
                                      $"SET Status='CANCELED' " +
                                      $"WHERE Status='NEW' OR Status='RUNNING'";
            try
            {
                using (MySqlConnection connection = new MySqlConnection(MySqlConnectionString))
                {
                    connection.Open();
                    using (MySqlCommand command = new MySqlCommand(updateTasksRows, connection))
                    {
                        command.ExecuteNonQuery();
                    }
                    using (MySqlCommand command = new MySqlCommand(updateModuleJobRow, connection))
                    {
                        command.ExecuteNonQuery();
                    }
                }
            }
            catch (SqlException sqlE)
            {
                Error("CancelModuleTasks", "Encountered an issue with SQL", sqlE);
                return false;
            }
            catch (MySqlException sqlE)
            {
                Error("CancelModuleTasks", "Encountered an issue with SQL", sqlE);
                return false;
            }
            catch (Exception e)
            {
                Error("CancelModuleTasks", "Unknown issue", e);
                return false;
                //return new ErrorValue("Unknown issue", e);
            }

            return true;
        }

        /// <summary>
        /// Add a task for the driver
        /// </summary>
        /// <param name="TaskID"></param>
        /// <param name="type">OPTIONS: Forward,Backward,Left,Right,Stop</param>
        /// <param name="dist">v>0 a dist in CM, v<0 means "infinite dist" (until Stop)</param>
        /// <param name="speed">value 0<=speed<=127 that the driver servos get</param>
        /// <returns>success/failure</returns>
        public static bool AddDriverTask(string TaskID, string type, int dist, int speed, int time, int quadrant, int radius)
        {
            List<string> lines = new List<string>();
            // Add task to driver and THEN update moduleJobs (to avoid driver not finding the task)
            string addDriverTask = "INSERT INTO DriverTasks (TaskID, Command, Value, Speed, Time, Quadrant, Radius, Status) " +
                $"Values ('{TaskID}', '{type}', '{dist}', '{speed}', '{time}', '{quadrant}', '{radius}', 'NEW')";

            string updateModuleJobRow = "UPDATE modulejobs " +
                                          $"SET ManagerStatus=1 " +
                                          $"WHERE Module='Driver'";
            try
            {
                using (MySqlConnection connection = new MySqlConnection(MySqlConnectionString))
                {
                    connection.Open();
                    using (MySqlCommand command = new MySqlCommand(addDriverTask, connection))
                    {
                        command.ExecuteNonQuery();
                    }
                    using (MySqlCommand command = new MySqlCommand(updateModuleJobRow, connection))
                    {
                        command.ExecuteNonQuery();
                    }
                }
            }
            catch (SqlException sqlE)
            {
                Error("AddDriverTask", "Encountered an issue with SQL", sqlE);
                return false;
            }
            catch (MySqlException sqlE)
            {
                Error("AddDriverTask", "Encountered an issue with SQL", sqlE);
                return false;
            }
            catch (Exception e)
            {
                Error("AddDriverTask", "Unknown issue", e);
                return false;
                //return new ErrorValue("Unknown issue", e);
            }

            return true;
        }

        /// <summary>
        /// Add a task for the Maintenance
        /// </summary>
        /// <param name="TaskID"></param>
        /// <param name="type">OPTIONS:</param>
        /// <param name="value">v>0 an angle in DEG, v<0 means "keep turning" (until Stop)</param>
        /// <param name="speed">value 0<=speed that the crane servos get</param>
        /// <param name="time"></param>
        /// <returns>success/failure</returns>
        public static bool AddCraneTask(string TaskID, string type, int value, int speed, int time, string data = "")
        {
            List<string> lines = new List<string>();
            // Add task to driver and THEN update moduleJobs (to avoid driver not finding the task)
            string addDriverTask = "INSERT INTO CraneTasks (TaskID, Command, Value, Speed, Time, Data, Status) " +
                $"VALUES ('{TaskID}', '{type}', '{value}', '{speed}', '{time}', '{data}', 'NEW')";

            string updateModuleJobRow = "UPDATE modulejobs " +
                                          $"SET ManagerStatus=1 " +
                                          $"WHERE Module='Crane'";
            try
            {
                using (MySqlConnection connection = new MySqlConnection(MySqlConnectionString))
                {
                    connection.Open();
                    using (MySqlCommand command = new MySqlCommand(addDriverTask, connection))
                    {
                        command.ExecuteNonQuery();
                    }
                    using (MySqlCommand command = new MySqlCommand(updateModuleJobRow, connection))
                    {
                        command.ExecuteNonQuery();
                    }
                }
            }
            catch (SqlException sqlE)
            {
                Error("AddCraneTask", "Encountered an issue with SQL", sqlE);
                return false;
            }
            catch (MySqlException sqlE)
            {
                Error("AddCraneTask", "Encountered an issue with SQL", sqlE);
                return false;
            }
            catch (Exception e)
            {
                Error("AddCraneTask", "Unknown issue", e);
                return false;
                //return new ErrorValue("Unknown issue", e);
            }

            return true;
        }

        public static bool AddCameraTask(string TaskID, string type, int value, string saveLocally, string resolution)
        {
            List<string> lines = new List<string>();
            // Add task to driver and THEN update moduleJobs (to avoid driver not finding the task)
            string addDriverTask = "INSERT INTO CameraTasks (TaskID, Command, Value, Resolution, Status) " +
                                  $"VALUES ('{TaskID}', '{type}', '{value}', '{resolution}', 'NEW')";
            string addCommunicationTask = "INSERT INTO communicationout (TimeStamp, Type, FilePath, Data, Status) " +
                     $"VALUES ({DateTimeOffset.Now.ToUnixTimeMilliseconds()}, 'SEND_IMG', '', '{type},{saveLocally},{resolution}', 'NEW')";

            string updateModuleJobRow = "UPDATE modulejobs " +
                                          $"SET ManagerStatus=1 " +
                                          $"WHERE Module='Camera' OR Module='Communication'";
            try
            {
                using (MySqlConnection connection = new MySqlConnection(MySqlConnectionString))
                {
                    connection.Open();
                    using (MySqlCommand command = new MySqlCommand(addDriverTask, connection))
                    {
                        command.ExecuteNonQuery();
                    }
                    using (MySqlCommand command = new MySqlCommand(addCommunicationTask, connection))
                    {
                        command.ExecuteNonQuery();
                    }
                    using (MySqlCommand command = new MySqlCommand(updateModuleJobRow, connection))
                    {
                        command.ExecuteNonQuery();
                    }
                }
            }
            catch (SqlException sqlE)
            {
                Error("AddCameraTask", "Encountered an issue with SQL", sqlE);
                return false;
            }
            catch (MySqlException sqlE)
            {
                Error("AddCameraTask", "Encountered an issue with SQL", sqlE);
                return false;
            }
            catch (Exception e)
            {
                Error("AddCameraTask", "Unknown issue", e);
                return false;
                //return new ErrorValue("Unknown issue", e);
            }

            return true;
        }

        // used after the camera module tells manager it finished its camera task, this tells communication
        // to stop trying to send images
        public static bool AddCameraTaskDone()
        {
            List<string> lines = new List<string>();
            // Add task to driver and THEN update moduleJobs (to avoid driver not finding the task)
            string addCommunicationTask = "INSERT INTO communicationout (TimeStamp, Type, FilePath, Data, Status) " +
                      $"VALUES ({DateTimeOffset.Now.ToUnixTimeMilliseconds()}, 'SEND_IMG', '', 'Stop,False,False', 'NEW')";

            string updateModuleJobRow = "UPDATE modulejobs " +
                                          $"SET ManagerStatus=1 " +
                                          $"WHERE Module='Communication'";
            try
            {
                using (MySqlConnection connection = new MySqlConnection(MySqlConnectionString))
                {
                    connection.Open();
                    using (MySqlCommand command = new MySqlCommand(addCommunicationTask, connection))
                    {
                        command.ExecuteNonQuery();
                    }
                    using (MySqlCommand command = new MySqlCommand(updateModuleJobRow, connection))
                    {
                        command.ExecuteNonQuery();
                    }
                }
            }
            catch (MySqlException sqlE)
            {
                Error("AddCameraTaskDone", "Encountered an issue with SQL", sqlE);
                return false;
            }
            catch (Exception e)
            {
                Error("AddCameraTaskDone", "Unknown issue", e);
                return false;
                //return new ErrorValue("Unknown issue", e);
            }

            return true;
        }

        public static bool AddNavigationTask(string TaskID, string type)
        {
            List<string> lines = new List<string>();
            // Add task to navigation and THEN update moduleJobs (to avoid navigation not finding the task)
            string addNavigationTask = "INSERT INTO navigationtasks (TaskID, Command, Status) " +
                                       $"VALUES ('{TaskID}', '{type}', 'NEW')";

            string updateModuleJobRow = "UPDATE modulejobs " +
                                        $"SET ManagerStatus=1 " +
                                        $"WHERE Module='Navigation'";
            try
            {
                using (MySqlConnection connection = new MySqlConnection(MySqlConnectionString))
                {
                    connection.Open();
                    using (MySqlCommand command = new MySqlCommand(addNavigationTask, connection))
                    {
                        command.ExecuteNonQuery();
                    }
                    using (MySqlCommand command = new MySqlCommand(updateModuleJobRow, connection))
                    {
                        command.ExecuteNonQuery();
                    }
                }
            }
            catch (SqlException sqlE)
            {
                Error("AddNavigationTask", "Encountered an issue with SQL", sqlE);
                return false;
            }
            catch (MySqlException sqlE)
            {
                Error("AddNavigationTask", "Encountered an issue with SQL", sqlE);
                return false;
            }
            catch (Exception e)
            {
                Error("AddNavigationTask", "Unknown issue", e);
                return false;
                //return new ErrorValue("Unknown issue", e);
            }

            return true;
        }

        /// <summary>
        /// Add a db clean task for Maintenace
        /// </summary>
        /// <returns></returns>
        public static bool AddMaintenanceDbClean()
        {
            List<string> lines = new List<string>();
            // Add task to driver and THEN update moduleJobs (to avoid driver not finding the task)
            string addDriverTask = "INSERT INTO MaintenanceTasks (TimeStamp, Command, Status) " +
                $"Values ('{DateTimeOffset.Now.ToUnixTimeMilliseconds()}', 'CLEAN_DB', 'NEW')";

            string updateModuleJobRow = "UPDATE modulejobs " +
                                          $"SET ManagerStatus=1 " +
                                          $"WHERE Module='Maintenance'";
            try
            {
                using (MySqlConnection connection = new MySqlConnection(MySqlConnectionString))
                {
                    connection.Open();
                    using (MySqlCommand command = new MySqlCommand(addDriverTask, connection))
                    {
                        command.ExecuteNonQuery();
                    }
                    using (MySqlCommand command = new MySqlCommand(updateModuleJobRow, connection))
                    {
                        command.ExecuteNonQuery();
                    }
                }
            }
            catch (SqlException sqlE)
            {
                Error("AddMaintenanceDbClean", "Encountered an issue with SQL", sqlE);
                return false;
            }
            catch (MySqlException sqlE)
            {
                Error("AddMaintenanceDbClean", "Encountered an issue with SQL", sqlE);
                return false;
            }
            catch (Exception e)
            {
                Error("AddMaintenanceDbClean", "Unknown issue", e);
                return false;
                //return new ErrorValue("Unknown issue", e);
            }

            return true;
        }

        /// <summary>
        /// Add a task for sensors (right now only settings)
        /// </summary>
        /// <param name="type">SETTINGS</param>
        /// <param name="sensorsDataRate">time in seconds of the sample interval (>0)</param>
        public static bool AddSensorsTask(string RequestID, string type, double sensorsDataRate)
        {
            List<string> lines = new List<string>();
            // Add task to driver and THEN update moduleJobs (to avoid driver not finding the task)
            string addSensorsTask = "INSERT INTO SensorsTasks (TaskID, Command, Value, Status) " +
                $"Values ('{RequestID}', '{type}', '{sensorsDataRate}', 'NEW')";

            string updateModuleJobRow = "UPDATE modulejobs " +
                                          $"SET ManagerStatus=1 " +
                                          $"WHERE Module='Sensors'";
            try
            {
                using (MySqlConnection connection = new MySqlConnection(MySqlConnectionString))
                {
                    connection.Open();
                    using (MySqlCommand command = new MySqlCommand(addSensorsTask, connection))
                    {
                        command.ExecuteNonQuery();
                    }
                    using (MySqlCommand command = new MySqlCommand(updateModuleJobRow, connection))
                    {
                        command.ExecuteNonQuery();
                    }
                }
            }
            catch (SqlException sqlE)
            {
                Error("AddSensorsTask", "Encountered an issue with SQL", sqlE);
                return false;
            }
            catch (MySqlException sqlE)
            {
                Error("AddSensorsTask", "Encountered an issue with SQL", sqlE);
                return false;
            }
            catch (Exception e)
            {
                Error("AddSensorsTask", "Unknown issue", e);
                return false;
            }

            return true;
        }

        /// <summary>
        /// updates the the communicationin table rows as checked
        /// </summary>
        /// <param name="IDs"></param>
        /// <returns></returns>
        public static bool UpdateCommunicationInChecked(List<int> IDs)
        {
            List<string> lines = new List<string>();
            string IDList = string.Join(", ", IDs);
            // update all checked rows in the communicationin to have a 'status=done' state
            string updateAllProcessedIDs = "UPDATE communicationin " +
                                          $"SET Status='DONE' " +
                                          $"WHERE ID in ({IDList})";

            try
            {
                using (MySqlConnection connection = new MySqlConnection(MySqlConnectionString))
                {
                    connection.Open();
                    using (MySqlCommand command = new MySqlCommand(updateAllProcessedIDs, connection))
                    {
                        command.ExecuteNonQuery();
                    }
                }
            }
            catch (SqlException sqlE)
            {
                Error("UpdateCommunicationInChecked", "Encountered an issue with SQL", sqlE);
                return false;
            }
            catch (MySqlException sqlE)
            {
                Error("UpdateCommunicationInChecked", "Encountered an issue with SQL", sqlE);
                return false;
            }
            catch (Exception e)
            {
                Error("UpdateCommunicationInChecked", "Unknown issue", e);
                return false;
                //return new ErrorValue("Unknown issue", e);
            }

            return true;
        }

        public static bool AddRequestDbDone(int statusNumber, string dbPath)
        {
            string Updatemodulejobs = "UPDATE modulejobs " +
                          $"SET ManagerStatus={statusNumber} " +
                          "Where Module='Communication'";
            string Insertcommunicationin = "INSERT INTO communicationout (TimeStamp, Type, FilePath, Data, Status) " +
                $"Values ({DateTimeOffset.Now.ToUnixTimeMilliseconds()}, 'REQUEST_DB', '{dbPath}', '', 'NEW')";

            try
            {
                using (MySqlConnection connection = new MySqlConnection(MySqlConnectionString))
                {
                    connection.Open();
                    using (MySqlCommand command = new MySqlCommand(Insertcommunicationin, connection))
                    {
                        command.ExecuteNonQuery();
                    }
                    using (MySqlCommand command = new MySqlCommand(Updatemodulejobs, connection))
                    {
                        command.ExecuteNonQuery();
                    }
                }
            }
            catch (SqlException sqlE)
            {
                Error("AddRequestDbDone", "Encountered an issue with SQL", sqlE);
                return false;
            }
            catch (MySqlException sqlE)
            {
                Error("AddRequestDbDone", "Encountered an issue with SQL", sqlE);
                return false;
            }
            catch (Exception e)
            {
                Error("AddRequestDbDone", "Unknown issue", e);
                return false;
            }

            return true;
        }

        public static bool AddSettingsChanged(int statusNumber, string module)
        {
            string Updatemodulejobs = "UPDATE modulejobs " +
                          $"SET ManagerStatus={statusNumber} " +
                          "Where Module='Communication'";
            string Insertcommunicationin = "INSERT INTO communicationout (TimeStamp, Type, FilePath, Data, Status) " +
                $"Values ({DateTimeOffset.Now.ToUnixTimeMilliseconds()}, 'SETTINGS', '', '{module}', 'NEW')";

            try
            {
                // linux uses <MySql___> and windows uses <Sql___>, this is the best way I could figure out to write this section
                using (MySqlConnection connection = new MySqlConnection(MySqlConnectionString))
                {
                    connection.Open();
                    using (MySqlCommand command = new MySqlCommand(Insertcommunicationin, connection))
                    {
                        command.ExecuteNonQuery();
                    }
                    using (MySqlCommand command = new MySqlCommand(Updatemodulejobs, connection))
                    {
                        command.ExecuteNonQuery();
                    }
                }
            }
            catch (SqlException sqlE)
            {
                Error("AddSettingsChanged", "Encountered an issue with SQL", sqlE);
                return false;
            }
            catch (MySqlException sqlE)
            {
                Error("AddSettingsChanged", "Encountered an issue with SQL", sqlE);
                return false;
            }
            catch (Exception e)
            {
                Error("AddSettingsChanged", "Unknown issue", e);
                return false;
            }

            return true;
        }

        public static bool AddExternalPacketDone(int statusNumber)
        {
            string Updatemodulejobs = "UPDATE modulejobs " +
                          $"SET ManagerStatus={statusNumber} " +
                          "Where Module='Communication'";
            string Insertcommunicationin = "INSERT INTO communicationout (TimeStamp, Type, FilePath, Data, Status) " +
                $"Values ({DateTimeOffset.Now.ToUnixTimeMilliseconds()}, 'EXTERNAL_PACKET', '', '', 'NEW')";

            try
            {
                using (MySqlConnection connection = new MySqlConnection(MySqlConnectionString))
                {
                    connection.Open();
                    using (MySqlCommand command = new MySqlCommand(Insertcommunicationin, connection))
                    {
                        command.ExecuteNonQuery();
                    }
                    using (MySqlCommand command = new MySqlCommand(Updatemodulejobs, connection))
                    {
                        command.ExecuteNonQuery();
                    }
                }
            }
            catch (SqlException sqlE)
            {
                Error("AddExternalPacketDone", "Encountered an issue with SQL", sqlE);
                return false;
            }
            catch (MySqlException sqlE)
            {
                Error("AddExternalPacketDone", "Encountered an issue with SQL", sqlE);
                return false;
            }
            catch (Exception e)
            {
                Error("AddExternalPacketDone", "Unknown issue", e);
                return false;
            }

            return true;
        }

        public static bool AddSoftResetDone(int statusNumber)
        {
            string Updatemodulejobs = "UPDATE modulejobs " +
                          $"SET ManagerStatus={statusNumber} " +
                          "Where Module='Communication'";
            string Insertcommunicationin = "INSERT INTO communicationout (TimeStamp, Type, FilePath, Data, Status) " +
                $"Values ({DateTimeOffset.Now.ToUnixTimeMilliseconds()}, 'SOFT_RESET', '', '', 'NEW')";

            try
            {
                using (MySqlConnection connection = new MySqlConnection(MySqlConnectionString))
                {
                    connection.Open();
                    using (MySqlCommand command = new MySqlCommand(Insertcommunicationin, connection))
                    {
                        command.ExecuteNonQuery();
                    }
                    using (MySqlCommand command = new MySqlCommand(Updatemodulejobs, connection))
                    {
                        command.ExecuteNonQuery();
                    }
                }
            }
            catch (MySqlException sqlE)
            {
                Error("AddSoftResetDone", "Encountered an issue with SQL", sqlE);
                return false;
            }
            catch (Exception e)
            {
                Error("AddSoftResetDone", "Unknown issue", e);
                return false;
            }

            return true;
        }

        public static bool AddManagerAction(string ActionID, string Module, string ActionType, string Action, string Status, string Details)
        {
            string InsertManagerActions = "INSERT INTO ManagerActions (TimeStamp, ActionID, Module, ActionType, Action, Status, Details) " +
                $"Values ({DateTimeOffset.Now.ToUnixTimeMilliseconds()}, '{ActionID}', '{Module}', '{ActionType}', '{Action}', '{Status}', '{Details}')";

            try
            {
                // linux uses <MySql___> and windows uses <Sql___>, this is the best way I could figure out to write this section
                using (MySqlConnection connection = new MySqlConnection(MySqlConnectionString))
                {
                    connection.Open();
                    using (MySqlCommand command = new MySqlCommand(InsertManagerActions, connection))
                    {
                        command.ExecuteNonQuery();
                    }
                }
            }
            catch (SqlException sqlE)
            {
                Error("AddManagerAction", "Encountered an issue with SQL", sqlE);
                return false;
            }
            catch (MySqlException sqlE)
            {
                Error("AddManagerAction", "Encountered an issue with SQL", sqlE);
                return false;
            }
            catch (Exception e)
            {
                Error("AddManagerAction", "Unknown issue", e);
                return false;
            }

            return true;
        }
    }
}
