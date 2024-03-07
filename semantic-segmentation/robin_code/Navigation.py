"""!
@brief Version 1.0 of the Navigation module.
Starts navigation when it gets the order to do so by the manager.

This is the main file. It contains a Navigation object, that handles the navigation.
"""

import threading
import time
from navigation_inner import Navigation


##############################
# Globals
##############################


nav = Navigation()


##############################
# Thread Functions
##############################


# Creates a thread to look for new tasks
def look_for_tasks():
    # try:
    nav.connect_to_db()
    nav.write_log("Started look_for_tasks thread and connected to DB.")

    while True:
        # ManagerStatus in ModuleJobs table notifies on new tasks
        nav.tasks_cursor.execute("SELECT ManagerStatus FROM modulejobs WHERE Module='Navigation'")
        result = nav.tasks_cursor.fetchall()

        # Parse Navigation module line
        if len(result) == 1:
            for (ManagerStatus, ) in result:
                manager_status = ManagerStatus
                if manager_status == 1:
                    # New tasks are in the NavigationTasks table
                    nav.tasks_cursor.execute("SELECT TaskID, Command "
                                             "FROM navigationtasks "
                                             "WHERE Status='NEW'")
                    result = nav.tasks_cursor.fetchall()

                    # Iterate new tasks
                    if len(result) > 0:
                        for (TaskID, Command) in result:
                            if Command == "Start":
                                # TODO: START CAMERA??
                                nav.start()

                                # Set Start Navigation task to RUNNING
                                nav.start_task_id = TaskID
                                nav.tasks_cursor.execute("UPDATE navigationtasks "
                                                         "SET Status='RUNNING' "
                                                         f"WHERE TaskID='{TaskID}'")

                            elif Command == "Stop":
                                # TODO: STOP CAMERA??
                                nav.stop()
                                # Set Start Navigation and Stop Navigation tasks to DONE
                                nav.tasks_cursor.execute("UPDATE navigationtasks "
                                                         "SET Status='DONE' "
                                                         f"WHERE TaskID='{TaskID}' OR TaskID='{nav.start_task_id}'")

                                # Update ModuleJobs, and set 1st bit of ModuleStatus to 1
                                nav.tasks_cursor.execute("UPDATE modulejobs "
                                                         "SET ManagerStatus=0, ModuleStatus = ModuleStatus | 1 "
                                                         "WHERE Module='Navigation'")

                                # Reset TaskID info
                                nav.start_task_id = ""
                                nav.request_i = 1
                                nav.request_task_id = ""

                        # Update ModuleJobs that all pending tasks have been executed
                        nav.tasks_cursor.execute("UPDATE modulejobs "
                                                 "SET ManagerStatus=0, ModuleStatus = ModuleStatus | 1 "
                                                 "WHERE Module='Navigation'")

        # Commit SQL changes and sleep to avoid flooding the SQL server
        nav.database.commit()
        time.sleep(0.1)

    # # Errors
    # except Exception as e:
    #     nav.write_log("Something else went wrong at 'look_for_tasks': " + str(e))
    #
    # finally:
    #     nav.disconnect_from_db()


if __name__ == '__main__':
    # Set the log dir
    nav.write_log("Navigation module starting!")
    nav.write_log(f"Using the log directory: {nav.logfile}")

    # Start the task thread and wait for it
    th = threading.Thread(target=look_for_tasks)
    th.start()
    th.join()  # wait until it finishes
    # TODO should I check if it finished bc it crashed?

    # cnx_main.close()
    nav.write_log("Main closed. Bye!")
    quit()
