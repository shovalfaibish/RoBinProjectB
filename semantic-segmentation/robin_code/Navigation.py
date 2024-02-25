"""!
@brief Version 1.0 of the Navigation module.
Starts navigation when it gets the order to do so by the manager.

This is the main file. It contains a Navigation object, that handles the navigation.
"""

import threading
import time
import os
import mysql.connector
from pathlib import Path
from navigation_inner import Navigation


##############################
# Globals
##############################


logfile = os.path.join(Path.home(), "/RoBin_Files/Logs/Latest/Navigation_Logs.txt")
start_TaskID = ""
nav = Navigation()


##############################
# Thread Functions
##############################


# Creates a thread to look for new tasks
def look_for_tasks():
    try:
        global start_TaskID

        # Connect to SQL server
        cnx_tasks = mysql.connector.connect(user='robin', password='robin',
                                            host='127.0.0.1',
                                            database='RobinDB')
        cursor_tasks = cnx_tasks.cursor()
        write_log("Started main task thread and connected it to SQL.")

        while True:
            # ManagerStatus in ModuleJobs table notifies on new tasks
            cursor_tasks.execute("SELECT ManagerStatus FROM ModuleJobs WHERE Module='Navigation'")
            result = cursor_tasks.fetchall()

            # Parse Navigation module line
            if len(result) == 1:
                for (ManagerStatus, ) in result:
                    manager_status = ManagerStatus
                    if manager_status == 1:
                        # New tasks are in the NavigationTasks table
                        cursor_tasks.execute("SELECT TaskID, Command "
                                             "FROM NavigationTasks "
                                             "WHERE Status='NEW'")
                        result = cursor_tasks.fetchall()

                        # Iterate new tasks
                        if len(result) > 0:
                            for (TaskID, Command) in result:
                                if Command == "StartNavigation":
                                    # TODO: START CAMERA??
                                    nav.start()

                                    # Set StartNavigation task to RUNNING
                                    start_TaskID = TaskID
                                    cursor_tasks.execute("UPDATE NavigationTasks "
                                                         "SET Status='RUNNING' "
                                                         f"WHERE TaskID='{TaskID}'")

                                elif Command == "StopNavigation":
                                    # TODO: STOP CAMERA??
                                    nav.stop()

                                    # Set StartNavigation and StartNavigation tasks to DONE
                                    cursor_tasks.execute("UPDATE NavigationTasks "
                                                         "SET Status='DONE' "
                                                         f"WHERE TaskID={TaskID} OR TaskID={start_TaskID}")

                                    # Update ModuleJobs on module stopped
                                    cursor_tasks.execute("UPDATE ModuleJobs "
                                                         "SET ManagerStatus=0, ModuleStatus=1 "
                                                         "WHERE Module='Navigation'")
                                    start_TaskID = ""

                            # Update ModuleJobs that all pending tasks have been executed
                            cursor_tasks.execute("UPDATE ModuleJobs "
                                                 "SET ManagerStatus=0, ModuleStatus=1 "
                                                 "WHERE Module='Navigation'")

            # Commit SQL changes and sleep to avoid flooding the SQL server
            cnx_tasks.commit()
            time.sleep(0.1)

    # Errors
    except mysql.connector.Error as err:
        write_log(f"Something went wrong with MySQL: {err}")

    except Exception as e:
        write_log("Something else went wrong at 'look_for_tasks' :" + str(e))

    finally:
        cnx_tasks.close()


def write_log(msg):
    # Write to file and flush to stdout
    with open(logfile, 'a') as f:
        f.write(msg + '\n')
    print(msg, flush=True)


if __name__ == '__main__':
    # Set the log dir
    write_log("Navigation module starting!")
    write_log(f"Using the log directory: {os.path.dirname(logfile)}")

    # Start the task thread and wait for it
    th = threading.Thread(target=look_for_tasks)
    th.start()
    th.join()  # wait until it finishes
    # TODO should I check if it finished bc it crashed?

    # cnx_main.close()
    write_log("Main closed. Bye!")
    quit()
