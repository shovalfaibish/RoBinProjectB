"""!
@brief Version 1.0 of the Navigation module.
Starts navigation when it gets the order to do so by the manager.

This is the main file. It contains a Navigation object, that handles the navigation.
"""

import threading
import time
from navigation_inner import Navigation
from datetime import datetime


##############################
# Globals
##############################


nav = Navigation()
# TODO: ADD SEMSEG ON CONSTANT IMAGE AT STARTUP


##############################
# Thread Function
##############################


# Creates a thread to look for new tasks
def look_for_tasks():
    # Connection to DB
    t_database, tasks_cursor = None, None

    try:
        t_database, tasks_cursor = nav.create_connection_to_db()
        nav.write_log("Started look_for_tasks thread and connected to DB.")

        # DELETE EVENTUALLY
        tasks_cursor.execute("delete from navigationrequests where Status='RUNNING' or Status='DONE'")
        tasks_cursor.execute("delete from navigationtasks where Status = 'RUNNING' or Status = 'NEW'")
        tasks_cursor.execute("delete from communicationin where Status = 'DONE'")
        dtime = datetime.now().strftime("%d%m%y_%H%M%S%f")
        tasks_cursor.execute(f"insert into communicationin (TimeStamp, Type, Data, Status) values ('1', 'MANUAL', '2,{dtime},Navigation,Start', 'NEW')")
        tasks_cursor.execute("update modulejobs set ModuleStatus=1 where Module='Communication'")
        t_database.commit()

        while True:
            # ManagerStatus in ModuleJobs table notifies on new tasks
            tasks_cursor.execute("SELECT ManagerStatus FROM modulejobs WHERE Module='Navigation'")
            result = tasks_cursor.fetchall()

            # Parse Navigation module line
            if len(result) == 1:
                for (ManagerStatus, ) in result:
                    manager_status = ManagerStatus
                    if manager_status == 1:
                        # New tasks are in the NavigationTasks table
                        tasks_cursor.execute("SELECT TaskID, Command "
                                             "FROM navigationtasks "
                                             "WHERE Status='NEW'")
                        result = tasks_cursor.fetchall()

                        # Iterate new tasks
                        if len(result) > 0:
                            for (TaskID, Command) in result:
                                if Command == "Start":
                                    # TODO: START CAMERa start_internal using request
                                    nav.start()

                                    # Set Start Navigation task to RUNNING
                                    nav.start_task_id = TaskID
                                    tasks_cursor.execute("UPDATE navigationtasks "
                                                         "SET Status='RUNNING' "
                                                         f"WHERE TaskID='{TaskID}'")

                                elif Command == "Stop":
                                    # TODO: STOP CAMERA?? wait for it to signal done, then continue
                                    nav.stop()
                                    # Set Start Navigation and Stop Navigation tasks to DONE
                                    tasks_cursor.execute("UPDATE navigationtasks "
                                                         "SET Status='DONE' "
                                                         f"WHERE TaskID='{TaskID}' OR TaskID='{nav.start_task_id}'")

                                    # Update ModuleJobs, and set 1st bit of ModuleStatus to 1
                                    tasks_cursor.execute("UPDATE modulejobs "
                                                         "SET ManagerStatus=0, ModuleStatus = ModuleStatus | 1 "
                                                         "WHERE Module='Navigation'")

                                    # Reset TaskID info
                                    nav.start_task_id = ""
                                    nav.request_i = 1
                                    nav.request_task_id = ""

                            # Update ModuleJobs that all pending tasks have been executed
                            tasks_cursor.execute("UPDATE modulejobs "
                                                 "SET ManagerStatus=0, ModuleStatus = ModuleStatus | 1 "
                                                 "WHERE Module='Navigation'")

            # Commit SQL changes and sleep to avoid flooding the SQL server
            t_database.commit()
            time.sleep(0.1)

    # Errors
    except Exception as e:
        nav.write_log("Error in function 'look_for_tasks': " + str(e))

    finally:
        if t_database is not None:
            nav.disconnect_from_db(nav.r_database, nav.requests_cursor)
            nav.disconnect_from_db(t_database, tasks_cursor)


if __name__ == '__main__':
    nav.write_log("Navigation module starting!")
    nav.write_log(f"Using the log directory: {nav.logfile}")

    # Start task thread
    th = threading.Thread(target=look_for_tasks)
    th.start()
    th.join()  # wait until it finishes
    # TODO should I check if it finished bc it crashed?

    nav.write_log("Main closed. Bye!")
    quit()
