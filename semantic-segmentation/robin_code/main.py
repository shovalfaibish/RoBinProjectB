import time
from navigation_inner import Navigation


if __name__ == '__main__':
    nav = Navigation(lab_exp=True)
    nav.start()
    time.sleep(20)
    nav.stop()
