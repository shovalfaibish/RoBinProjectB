using System;
using System.Collections.Generic;
using System.Data.SqlClient;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

//using System.Runtime.InteropServices; // tells us if we're on Windows OR Linux
using System.Data;
using MySql.Data.MySqlClient;

namespace CommunicationGUI.Utility
{
    static class SqlHelper
    {
        static readonly string MySqlConnectionString = System.Configuration.ConfigurationManager.ConnectionStrings["RobinDB_MySQL"].ConnectionString;
        static readonly string SqlConnectionString = System.Configuration.ConfigurationManager.ConnectionStrings["RobinDB_SQL"].ConnectionString;

        public delegate void EventLog(string function, string Msg);
        public static event EventLog Tracking;
        public delegate void EventErrorLog(string function, string Msg, Exception e = null);
        public static event EventErrorLog Error;

        public static async Task<bool> UpdateCommandReceived(int statusNumber, string cmd, string filePath, string data)
        {
            try
            {
                string UpdateModuleJobs = "UPDATE modulejobs " +
                                          $"SET ModuleStatus={statusNumber} " +
                                          "Where Module='Communication'";
                string InsertCommunicationIn = "INSERT INTO communicationin (TimeStamp, Type, FilePath, Data, Status) " +
                    $"Values ({DateTimeOffset.Now.ToUnixTimeMilliseconds()}, '{cmd}', '{filePath}', '{data}', 'NEW')";




            using (MySqlConnection connection = new MySqlConnection(MySqlConnectionString))
		    {
		        connection.Open();
		        using (MySqlCommand command = new MySqlCommand(InsertCommunicationIn, connection))
		        {
		            await command.ExecuteNonQueryAsync();
		        }
		        using (MySqlCommand command = new MySqlCommand(UpdateModuleJobs, connection))
		        {
		            await command.ExecuteNonQueryAsync();
		        }
		    }               
            }
            catch (SqlException sqlE)
            {
                Error("UpdateCommandReceived", "Encountered an issue with SQL", sqlE);
                return false;
            }
            catch (MySqlException sqlE)
            {
                Error("UpdateCommandReceived", "Encountered an issue with SQL", sqlE);
                return false;
            }
            catch (Exception e)
            {
                Error("UpdateCommandReceived", "Unknown issue", e);
                return false;
            }

            return true;
        }
        /*
        /// <summary>
        /// update the SQL tables to reflect an External-Command being received  
        /// </summary>
        /// <param name="data"></param>
        /// <param name="statusNumber"></param>
        /// <returns></returns>
        public static async Task<bool> UpdateExternalReceived(string data, int statusNumber)
        {
            try
            {
                string UpdateModuleJobs = "UPDATE ModuleJobs " +
                                          $"SET ModuleStatus={statusNumber} " +
                                          "Where Module='Communication'";
                string InsertCommunicationIn = "INSERT INTO CommunicationIn (TimeStamp, Type, FilePath, Data, Processed) " +
                    $"Values ({DateTimeOffset.Now.ToUnixTimeMilliseconds()}, 'EXTERNAL', '', '{data}', 0)";


                    using (MySqlConnection connection = new MySqlConnection(MySqlConnectionString))
                    {
                        connection.Open();
                        using (MySqlCommand command = new MySqlCommand(InsertCommunicationIn, connection))
                        {
                            await command.ExecuteNonQueryAsync();
                        }
                        using (MySqlCommand command = new MySqlCommand(UpdateModuleJobs, connection))
                        {
                            await command.ExecuteNonQueryAsync();
                        }
                    }
            }
            catch (SqlException sqlE)
            {
                Error("UpdateExternalReceived", "Encountered an issue with SQL", sqlE);
                return false;
            }
            catch (MySqlException sqlE)
            {
                Error("UpdateExternalReceived", "Encountered an issue with SQL", sqlE);
                return false;
            }
            catch (Exception e)
            {
                Error("UpdateExternalReceived", "Unknown issue", e);
                return false;
            }

            return true;
        }

        public static async Task<bool> UpdateShutdownReceived(int statusNumber)
        {
            try
            {
                string UpdateModuleJobs = "UPDATE ModuleJobs " +
                                          $"SET ModuleStatus={statusNumber} " +
                                          "Where Module='Communication'";
                string InsertCommunicationIn = "INSERT INTO CommunicationIn (TimeStamp, Type, FilePath, Data, Processed) " +
                    $"Values ({DateTimeOffset.Now.ToUnixTimeMilliseconds()}, 'SHUTDOWN', '', '', 0)";


                    using (MySqlConnection connection = new MySqlConnection(MySqlConnectionString))
                    {
                        connection.Open();
                        using (MySqlCommand command = new MySqlCommand(InsertCommunicationIn, connection))
                        {
                            await command.ExecuteNonQueryAsync();
                        }
                        using (MySqlCommand command = new MySqlCommand(UpdateModuleJobs, connection))
                        {
                            await command.ExecuteNonQueryAsync();
                        }
                    }                
            }
            catch (SqlException sqlE)
            {
                Error("UpdateShutdownReceived", "Encountered an issue with SQL", sqlE);
                return false;
            }
            catch (MySqlException sqlE)
            {
                Error("UpdateShutdownReceived", "Encountered an issue with SQL", sqlE);
                return false;
            }
            catch (Exception e)
            {
                Error("UpdateShutdownReceived", "Unknown issue", e);
                return false;
            }

            return true;
        }

        public static async Task<bool> UpdateCalibrationReceived(string data, int statusNumber)
        {
            try
            {
                string UpdateModuleJobs = "UPDATE ModuleJobs " +
                                          $"SET ModuleStatus={statusNumber} " +
                                          "Where Module='Communication'";
                string InsertCommunicationIn = "INSERT INTO CommunicationIn (TimeStamp, Type, FilePath, Data, Processed) " +
                    $"Values ({DateTimeOffset.Now.ToUnixTimeMilliseconds()}, 'CALIBRATE', '', '{data}', 0)";


                    using (MySqlConnection connection = new MySqlConnection(MySqlConnectionString))
                    {
                        connection.Open();
                        using (MySqlCommand command = new MySqlCommand(InsertCommunicationIn, connection))
                        {
                            await command.ExecuteNonQueryAsync();
                        }
                        using (MySqlCommand command = new MySqlCommand(UpdateModuleJobs, connection))
                        {
                            await command.ExecuteNonQueryAsync();
                        }
                    }                
            }
            catch (SqlException sqlE)
            {
                Error("UpdateCalibrationReceived", "Encountered an issue with SQL", sqlE);
                return false;
            }
            catch (MySqlException sqlE)
            {
                Error("UpdateCalibrationReceived", "Encountered an issue with SQL", sqlE);
                return false;
            }
            catch (Exception e)
            {
                Error("UpdateCalibrationReceived", "Unknown issue", e);
                return false;
            }

            return true;
        }

        public static async Task<bool> UpdateRequestDbReceived(int statusNumber)
        {
            try
            {
                string UpdateModuleJobs = "UPDATE ModuleJobs " +
                                          $"SET ModuleStatus={statusNumber} " +
                                          "Where Module='Communication'";
                string InsertCommunicationIn = "INSERT INTO CommunicationIn (TimeStamp, Type, FilePath, Data, Processed) " +
                    $"Values ({DateTimeOffset.Now.ToUnixTimeMilliseconds()}, 'REQUEST_DB', '', '', 0)";


                    using (MySqlConnection connection = new MySqlConnection(MySqlConnectionString))
                    {
                        connection.Open();
                        using (MySqlCommand command = new MySqlCommand(InsertCommunicationIn, connection))
                        {
                            await command.ExecuteNonQueryAsync();
                        }
                        using (MySqlCommand command = new MySqlCommand(UpdateModuleJobs, connection))
                        {
                            await command.ExecuteNonQueryAsync();
                        }
                    }                
            }
            catch (SqlException sqlE)
            {
                Error("UpdateRequestDbReceived", "Encountered an issue with SQL", sqlE);
                return false;
            }
            catch (MySqlException sqlE)
            {
                Error("UpdateRequestDbReceived", "Encountered an issue with SQL", sqlE);
                return false;
            }
            catch (Exception e)
            {
                Error("UpdateRequestDbReceived", "Unknown issue", e);
                return false;
            }

            return true;
        }

        public static async Task<bool> UpdateRequestDbCleanReceived(int statusNumber)
        {
            try
            {
                string UpdateModuleJobs = "UPDATE ModuleJobs " +
                                          $"SET ModuleStatus={statusNumber} " +
                                          "Where Module='Communication'";
                string InsertCommunicationIn = "INSERT INTO CommunicationIn (TimeStamp, Type, FilePath, Data, Processed) " +
                    $"Values ({DateTimeOffset.Now.ToUnixTimeMilliseconds()}, 'REQUEST_CLEAN', '', '', 0)";


                using (MySqlConnection connection = new MySqlConnection(MySqlConnectionString))
                {
                    connection.Open();
                    using (MySqlCommand command = new MySqlCommand(InsertCommunicationIn, connection))
                    {
                        await command.ExecuteNonQueryAsync();
                    }
                    using (MySqlCommand command = new MySqlCommand(UpdateModuleJobs, connection))
                    {
                        await command.ExecuteNonQueryAsync();
                    }
                }
            }
            catch (SqlException sqlE)
            {
                Error("UpdateRequestDbCleanReceived", "Encountered an issue with SQL", sqlE);
                return false;
            }
            catch (MySqlException sqlE)
            {
                Error("UpdateRequestDbCleanReceived", "Encountered an issue with SQL", sqlE);
                return false;
            }
            catch (Exception e)
            {
                Error("UpdateRequestDbCleanReceived", "Unknown issue", e);
                return false;
            }

            return true;
        }

        
        public static async Task<bool> UpdateSettingsReceived(string data, int statusNumber)
        {
            try
            {
                string UpdateModuleJobs = "UPDATE ModuleJobs " +
                                          $"SET ModuleStatus={statusNumber} " +
                                          "Where Module='Communication'";
                string InsertCommunicationIn = "INSERT INTO CommunicationIn (TimeStamp, Type, FilePath, Data, Processed) " +
                    $"Values ({DateTimeOffset.Now.ToUnixTimeMilliseconds()}, 'SETTINGS', '', '{data}', 0)";


                    using (MySqlConnection connection = new MySqlConnection(MySqlConnectionString))
                    {
                        connection.Open();
                        using (MySqlCommand command = new MySqlCommand(InsertCommunicationIn, connection))
                        {
                            await command.ExecuteNonQueryAsync();
                        }
                        using (MySqlCommand command = new MySqlCommand(UpdateModuleJobs, connection))
                        {
                            await command.ExecuteNonQueryAsync();
                        }
                    }                
            }
            catch (SqlException sqlE)
            {
                Error("UpdateSettingsReceived", "Encountered an issue with SQL", sqlE);
                return false;
            }
            catch (MySqlException sqlE)
            {
                Error("UpdateSettingsReceived", "Encountered an issue with SQL", sqlE);
                return false;
            }
            catch (Exception e)
            {
                Error("UpdateSettingsReceived", "Unknown issue", e);
                return false;
            }

            return true;
        }

        /// <summary>
        /// notify the manager about a project-file that represents a new OR updated project
        /// </summary>
        /// <param name="fileName">project-file name</param>
        public static async Task<bool> UpdateProjectReceived(string fileName, int statusNumber)
        {
            try
            {
                string UpdateModuleJobs = "UPDATE ModuleJobs " +
                                          $"SET ModuleStatus={statusNumber} " +
                                          "Where Module='Communication'";
                string UpdateCommunicationIn = "INSERT INTO CommunicationIn (TimeStamp, Type, FilePath, Data, Processed) " +
                    $"Values ({DateTimeOffset.Now.ToUnixTimeMilliseconds()}, 'PROJECT', '{fileName}', '', 0)";


                    using (MySqlConnection connection = new MySqlConnection(MySqlConnectionString))
                    {
                        connection.Open();
                        using (MySqlCommand command = new MySqlCommand(UpdateCommunicationIn, connection))
                        {
                            await command.ExecuteNonQueryAsync();
                        }
                        using (MySqlCommand command = new MySqlCommand(UpdateModuleJobs, connection))
                        {
                            await command.ExecuteNonQueryAsync();
                        }
                    }                
            }
            catch (SqlException sqlE)
            {
                Error("UpdateProjectReceived", "Encountered an issue with SQL", sqlE);
                return false;
            }
            catch (MySqlException sqlE)
            {
                Error("UpdateProjectReceived", "Encountered an issue with SQL", sqlE);
                return false;
            }
            catch (Exception e)
            {
                Error("UpdateProjectReceived", "Unknown issue", e);
                return false;
            }

            return true;
        }

        public static async Task<bool> UpdateCommandReceived(string directCmd)
        {
            try
            {
                string UpdateModuleJobs = "UPDATE ModuleJobs " +
                                          $"SET NewRequestRecieved=2 " +
                                          "Where Module='Communication'";
                string UpdateCommunicationIn = "INSERT INTO CommunicationIn (TimeStamp, Type, FilePath, Data) " +
                    $"Values ('{DateTimeOffset.Now.ToUnixTimeMilliseconds()}', 'COMMAND', '', '{directCmd}')";


                    using (MySqlConnection connection = new MySqlConnection(MySqlConnectionString))
                    {
                        connection.Open();
                        using (MySqlCommand command = new MySqlCommand(UpdateCommunicationIn, connection))
                        {
                            await command.ExecuteNonQueryAsync();
                        }
                        using (MySqlCommand command = new MySqlCommand(UpdateModuleJobs, connection))
                        {
                            await command.ExecuteNonQueryAsync();
                        }
                    }                
            }
            catch (SqlException sqlE)
            {
                Error("UpdateCommandReceived", "Encountered an issue with SQL", sqlE);
                return false;
            }
            catch (MySqlException sqlE)
            {
                Error("UpdateCommandReceived", "Encountered an issue with SQL", sqlE);
                return false;
            }
            catch (Exception e)
            {
                Error("UpdateCommandReceived", "Unknown issue", e);
                return false;
            }

            return true;
        }*/

        /// <summary>
        /// get the ModuleJobs column ManagerStatus, 'Communication' row (int value)
        /// its bits are boolean values of the different: tables (1-changes,0-no changes)
        /// </summary>
        /// <returns></returns>
        public static int? GetModuleJobsStatus()
        {
            List<int> lines = new List<int>();
            string CommandSTR = "Select * " +
                                "From modulejobs " +
                                "WHERE Module='Communication'";

            try
            {
                    using (MySqlConnection connection = new MySqlConnection(MySqlConnectionString))
                    {
                        connection.Open();
                        using (MySqlCommand command = new MySqlCommand(CommandSTR, connection))
                        {
                            using (MySqlDataReader reader = (MySqlDataReader)command.ExecuteReader())
                            {
                                while (reader.Read())
                                {
                                    lines.Add(reader.GetInt32(reader.GetOrdinal("ManagerStatus")));
                                }
                            }
                        }
                    }                
            }
            catch (SqlException sqlE)
            {
                Error("GetModuleJobsStatus", "Encountered an issue with SQL ", sqlE);
                return null;
                //return new ErrorValue<int?>(null, "Encountered an issue with SQL ", sqlE);
            }
            catch (MySqlException sqlE)
            {
                Error("GetModuleJobsStatus", "Encountered an issue with SQL ", sqlE);
                return null;
                //return new ErrorValue<int?>(null, "Encountered an issue with SQL ", sqlE);
            }
            catch (Exception e)
            {
                Error("GetModuleJobsStatus", "Unknown issue ", e);
                return null;
                //return new ErrorValue<int?>(null, "Unknown issue ", e);
            }

            if (lines.Count > 1 || lines.Count == 0)
            {
                Error("GetModuleJobsStatus", "Didn't get a single result as expected ");
                //return new ErrorValue<int?>(null, "Didn't get a single result as expected");
                return null;
            }

            //return new ErrorValue<int?>(lines[0]);
            return lines[0];
        }

        /// <summary>
        /// 
        /// </summary>
        /// <returns></returns>
        public static string GetLatestHBData()
        {
            string data = string.Empty;
            string CommandSTR = "Select * " +
                                "From sensorsdata " +
                                "ORDER BY ID DESC LIMIT 1";

            try
            {
                using (MySqlConnection connection = new MySqlConnection(MySqlConnectionString))
                {
                    connection.Open();
                    using (MySqlCommand command = new MySqlCommand(CommandSTR, connection))
                    {
                        using (MySqlDataReader reader = (MySqlDataReader)command.ExecuteReader())
                        {
                            while (reader.Read())
                            {
                                data = $"{reader.GetString(reader.GetOrdinal("TimeStamp"))}," +
                                       $"{reader.GetString(reader.GetOrdinal("GPS_Lat"))}," +
                                       $"{reader.GetString(reader.GetOrdinal("GPS_Lon"))}," +
                                       $"{reader.GetString(reader.GetOrdinal("GPS_Alt"))}," +
                                       $"{reader.GetString(reader.GetOrdinal("Yaw"))}," +
                                       $"{reader.GetString(reader.GetOrdinal("Pitch"))}," +
                                       $"{reader.GetString(reader.GetOrdinal("Roll"))}," +
                                       $"{reader.GetString(reader.GetOrdinal("Compass"))}";
                            }
                        }
                    }
                }
            }
            catch (SqlException sqlE)
            {
                Error("GetLatestGPSData", "Encountered an issue with SQL ", sqlE);
                return null;
                //return new ErrorValue<int?>(null, "Encountered an issue with SQL ", sqlE);
            }
            catch (MySqlException sqlE)
            {
                Error("GetLatestGPSData", "Encountered an issue with SQL ", sqlE);
                return null;
                //return new ErrorValue<int?>(null, "Encountered an issue with SQL ", sqlE);
            }
            catch (Exception e)
            {
                Error("GetLatestGPSData", "Unknown issue ", e);
                return null;
                //return new ErrorValue<int?>(null, "Unknown issue ", e);
            }

            if (data == string.Empty)
            {
                Error("GetLatestGPSData", "No Sensors data available");
                //return new ErrorValue<int?>(null, "Didn't get a single result as expected");
                return null;
            }

            //return new ErrorValue<int?>(lines[0]);
            return data;
        }        

        /// <summary>
        /// retrieves all the ModuleJob table rows to check for module updates
        /// </summary>
        /// <returns></returns>
        public static List<string> GetAllNewCommOuts()
        {
            List<string> lines = new List<string>();
            string CommandSTR = "Select * " +
                         "From communicationout " +
                         "WHERE Status='NEW'";

            try
            {
                    using (MySqlConnection connection = new MySqlConnection(MySqlConnectionString))
                    {
                        connection.Open();
                        using (MySqlCommand command = new MySqlCommand(CommandSTR, connection))
                        {
                            using (MySqlDataReader reader = (MySqlDataReader)command.ExecuteReader())
                            {
                                while (reader.Read())
                                {
                                    string data = $"{reader.GetInt32(reader.GetOrdinal("ID"))}|" +
                                                  $"{reader.GetString(reader.GetOrdinal("Type"))}|" +
                                                  $"{reader.GetString(reader.GetOrdinal("FilePath"))}|" +
                                                  $"{reader.GetString(reader.GetOrdinal("Data"))}";
                                    lines.Add(data);
                                }
                            }
                        }
                    }                
            }
            catch (SqlException sqlE)
            {
                Error("GetAllNewTasks", "Encountered an issue with SQL", sqlE);
                return null;
                //return new ErrorValue<List<string>>(null, "Encountered an issue with SQL ", sqlE);
            }
            catch (MySqlException sqlE)
            {
                Error("GetAllNewTasks", "Encountered an issue with SQL", sqlE);
                return null;
                //return new ErrorValue<List<string>>(null, "Encountered an issue with SQL ", sqlE);
            }
            catch (Exception e)
            {
                Error("GetAllNewTasks", "Unknown issue", e);
                return null;
                //return new ErrorValue<List<string>>(null, "Unknown issue ", e);
            }

            return lines;
            //return new ErrorValue<List<string>>(lines);
        }

        /// <summary>
        /// set a new ManagerStutus in ModuleJobs that means "I read the msgs"
        /// </summary>
        public static bool UpdateTasksRead()
        {
            List<string> lines = new List<string>();

            string ModuleJobsCmd = "UPDATE modulejobs " +
                                    "SET ManagerStatus=0 " +
                                    "WHERE Module='Communication'";


            try
            {
                    using (MySqlConnection connection = new MySqlConnection(MySqlConnectionString))
                    {
                        connection.Open();
                        using (MySqlCommand command = new MySqlCommand(ModuleJobsCmd, connection))
                        {
                            command.ExecuteNonQuery();
                        }
                    }                
            }
            catch (SqlException sqlE)
            {
                Error("UpdateTasksRead", "Encountered an issue with SQL", sqlE);
                return false;
                //return new ErrorValue("Encountered an issue with SQL", sqlE);
            }
            catch (MySqlException sqlE)
            {
                Error("UpdateTasksRead", "Encountered an issue with SQL", sqlE);
                return false;
                //return new ErrorValue("Encountered an issue with SQL", sqlE);
            }
            catch (Exception e)
            {
                Error("UpdateTasksRead", "Unknown issue", e);
                return false;
                //return new ErrorValue("Unknown issue", e);
            }

            return true;
        }

        /// <summary>
        /// update TaskIDs to be DONE in DriverTasks 
        /// and set a new ModuleStutus in ModuleJobs that means "Task done, status updated"
        /// </summary>
        /// <param name="module"></param>
        public static bool UpdateTaskToDone(int TaskID)
        {
            List<string> lines = new List<string>();

            // SQL needs a special format for a list of IDs
            string DriverTasksCmd = "UPDATE communicationout " +
                                    "SET Status='DONE' " +
                                   $"Where ID='{TaskID}'";
            /*string ModuleJobsCmd = "UPDATE ModuleJobs " +
                                    "SET ModuleStatus=1 " +
                                    "Where Module='Driver'";*/
            // TODO we dont need to consider notifying manager that we sent it, I think.

            try
            {
                    using (MySqlConnection connection = new MySqlConnection(MySqlConnectionString))
                    {
                        connection.Open();
                        using (MySqlCommand command = new MySqlCommand(DriverTasksCmd, connection))
                        {
                            command.ExecuteNonQuery();
                        }
                    }                
            }
            catch (SqlException sqlE)
            {
                Error("UpdateTaskToDone", "Encountered an issue with SQL", sqlE);
                return false;
                //return new ErrorValue("Encountered an issue with SQL", sqlE);
            }
            catch (MySqlException sqlE)
            {
                Error("UpdateTaskToDone", "Encountered an issue with SQL", sqlE);
                return false;
                //return new ErrorValue("Encountered an issue with SQL", sqlE);
            }
            catch (Exception e)
            {
                Error("UpdateTaskToDone", "Unknown issue", e);
                return false;
                //return new ErrorValue("Unknown issue", e);
            }

            return true;
        }

        /// <summary>
        /// set a new ManagerStutus/ModuleStatus in ModuleJobs
        /// </summary>
        public static async Task<bool> UpdateModuleJobsStatus(int ManagerStatus, int ModuleStatus, bool all = false)
        {
            bool managerChange = (ManagerStatus >= 0);
            bool moduleChange = (ModuleStatus >= 0);
            string setString = string.Empty;
            if (managerChange && moduleChange)
            {
                setString = $"SET ManagerStatus={ManagerStatus}, ModuleStatus={ModuleStatus} ";
            }
            else if (managerChange)
            {
                setString = $"SET ManagerStatus={ManagerStatus} ";
            }
            else if (moduleChange)
            {
                setString = $"SET ModuleStatus={ModuleStatus} ";
            }
            string ModuleJobsCmd = "UPDATE modulejobs " +
                                    setString;
            if (!all) ModuleJobsCmd += "WHERE Module='Maintenance'";


            try
            {
                using (MySqlConnection connection = new MySqlConnection(MySqlConnectionString))
                {
                    connection.Open();
                    using (MySqlCommand command = new MySqlCommand(ModuleJobsCmd, connection))
                    {
                        await command.ExecuteNonQueryAsync();
                    }
                }
            }
            catch (MySqlException sqlE)
            {
                Error("UpdateModuleJobsStatus", "Encountered an issue with SQL", sqlE);
                return false;
                //return new ErrorValue("Encountered an issue with SQL", sqlE);
            }
            catch (Exception e)
            {
                Error("UpdateModuleJobsStatus", "Unknown issue", e);
                return false;
                //return new ErrorValue("Unknown issue", e);
            }

            return true;
        }

        /// <summary>
        /// reset all tasks in these tables to be "done"
        /// </summary>
        /// <param name="module"></param>
        public static async Task<bool> ResetTable(string table)
        {
            string ResetTasksCommand = $"UPDATE {table} " +
                                        "SET Status='DONE_RESET' " +
                                        "WHERE Status IN('RUNNING','NEW')";

            try
            {
                using (MySqlConnection connection = new MySqlConnection(MySqlConnectionString))
                {
                    connection.Open();
                    using (MySqlCommand command = new MySqlCommand(ResetTasksCommand, connection))
                    {
                        await command.ExecuteNonQueryAsync();
                    }
                }
            }
            catch (MySqlException sqlE)
            {
                Error("ResetModule", "Encountered an issue with SQL", sqlE);
                return false;
                //return new ErrorValue("Encountered an issue with SQL", sqlE);
            }
            catch (Exception e)
            {
                Error("ResetModule", "Unknown issue", e);
                return false;
                //return new ErrorValue("Unknown issue", e);
            }

            return true;
        }
    }
}
