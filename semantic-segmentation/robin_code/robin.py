import time
import os
import datetime
import threading
import subprocess


class Groundstation:
    """
    Instantiate a Groundstation object.
    Used to communicate with the RoBin groundstation.

    :param __pipe_path: The path to the groundstation's named pipe.
    :type __pipe_path: string

    :param __connected: Did we already connect to the groundstation pipe?.
    :type __connected: bool

    :param _cmds: A list of the commands sent so far.
    :type _cmds: List of string

    :param _ind: Rising indicator for numbering the commands.
    :type _ind: int

    :param __driver: This object contains the commands that the driver can do.
    :type __driver: Driver

    :param __crane: This object contains the commands that the crane can do.
    :type __crane: Crane
    """

    def __init__(self, robin_name):
        self.__pipe_path = r'\\.\pipe\GroundstationPipe'
        self.__connected = False
        #self.__stop_writing = False
        self._cmds = []
        #self.thread_opener = threading.Thread(target=self.thread_active_writer)
        self._ind = 1
        self.__driver = Driver(self)
        self.__crane = Crane(self)
        self.__robin_name = robin_name
        self.ROBIN_HOME = "{}\\GroundStation\\External\\".format(os.getenv('APPDATA'))
        self.IMAGE_DIR = "{}\\GroundStation\\Images\\".format(os.getenv('APPDATA'))

    @property
    def driver(self):
        return self.__driver

    @property
    def crane(self):
        return self.__crane

    @property
    def ind(self):
        return self._ind

    @ind.setter
    def ind(self, value):
        self._ind = value

    @property
    def cmds(self):
        return self._cmds

    @cmds.setter
    def cmds(self, value):
        self._cmds = value;

    def _msg(self, msg):
        """
        Send a message to the GS pipe.

        :param msg: The msg to send to the pipe.
        :type msg: string
        """
        if self.__connected:
            with open(self.__pipe_path, 'w') as f:
                f.write(f'{msg}\n')
        else:
            return False
        # short sleep is needed to not flood the pipe between 2 consecutive msgs
        time.sleep(0.001)
        return True

    def connect(self):
        """
        Conncect to the GS pipe.
        """
        try:
            if not self.__connected:
                self.__connected = True
                self._msg("command,startup,Python")
            return True
        except Exception as e:
            print(e)
        return False

    def thread_active_writer(self):
        with open(self.__pipe_path, 'w') as f:
            self.__connected = True
            #self.thread_opener_started = True
            while True:
                if self.__stop_writing:
                    break
                #if len(self._to_write) > 0:
                #    msg = self._to_write.pop()
                #    f.write(f'{msg}\n')
                #    time.sleep(0.001)

    def start(self):
        if self.__connected:
            self._msg(f"command,external,{self.__robin_name},start")
            return True
        return False

    def stop(self):
        if self.__connected:
            self._msg(f"command,external,{self.__robin_name},stop")
            self.__stop_writing = True
            return True
        return False

    def frame(self):
        """
        :return: Latest frame as captured by the camera
        """
        image_list = [f for f in os.listdir(self.IMAGE_DIR) if os.path.isfile(os.path.join(self.IMAGE_DIR, f))]
        number_of_files = len(image_list)
        if number_of_files > 5:
            image_list.sort(key=lambda x: int(x.split('_')[1]))
            return self.IMAGE_DIR + image_list[-1]

    def export(self, filename):
        time_now = datetime.datetime.now()
        now_str = time_now.strftime("%d%m%Y%H%M%S")
        if not os.path.exists(self.ROBIN_HOME):
            print("error")
            # TODO ask for custom folder path? this folder should exists, though
        with open(self.ROBIN_HOME + f"{now_str}_" + filename, 'w') as f:
            for line in self.cmds:
                f.write(f"{line}\n")
        print(self.ROBIN_HOME + f"{now_str}_" + filename)

class Driver:
    """
    Class designed for use by the Groundstation class.
    Contains all the Driver methods.

    :param _ground: The "parent" ground object.
    :type _ground: Groundstation
    """
    def __init__(self, ground):
        self._ground = ground

    def forward(self, dist=-1):
        if dist < 0:
            return False
        cmd = f"{self._ground.ind},Driver,Forward,{dist}"
        return self.send_cmd(cmd)

    def backward(self, dist=-1):
        if dist < 0:
            return False
        cmd = f"{self._ground.ind},Driver,Backward,{dist}"
        return self.send_cmd(cmd)

    def left(self, angle=-1):
        if angle < 0:
            return False
        cmd = f"{self._ground.ind},Driver,Left,{angle}"
        return self.send_cmd(cmd)

    def right(self, angle=-1):
        if angle < 0:
            return False
        cmd = f"{self._ground.ind},Driver,Right,{angle}"
        return self.send_cmd(cmd)

    def curved(self, quadrant, radius, speed=3, dist=-1):
        cmd = f"{self._ground.ind},Driver,Curved,{dist},{speed},{quadrant},{radius}"
        return self.send_cmd(cmd)

    def send_cmd(self, cmd):
        if not self._ground._msg(cmd):
            return False
        self._ground.cmds.append(cmd)
        self._ground.ind += 1
        return True


class Crane:
    """
    Class designed for use by the Groundstation class.
    Contains all the Crane methods.

    :param _ground: The "parent" ground object.
    :type _ground: Groundstation
    """

    def __init__(self, ground):
        self._ground = ground

    def arm(self, value):
        cmd = f"{self._ground.ind},Crane,Arm,{value}"
        return self.send_cmd(cmd)

    def wrist(self, value):
        cmd = f"{self._ground.ind},Crane,Wrist,{value}"
        return self.send_cmd(cmd)

    def claw(self, value):
        cmd = f"{self._ground.ind},Crane,Claw,{value}"
        return self.send_cmd(cmd)

    def send_cmd(self, cmd):
        if not self._ground._msg(cmd):
            return False
        self._ground.cmds.append(cmd)
        self._ground.ind += 1
        return True