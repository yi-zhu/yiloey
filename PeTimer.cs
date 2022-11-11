using System;
using System.Collections.Generic;
using System.Timers;

namespace holey;


public class PeTimer {
    private Action<string> _taskLog;
    private Action<Action<int>, int> _taskHandle;
    private static readonly string LockTid = "lockTid";
    private DateTime _startDateTime = new DateTime(1970, 1, 1, 0, 0, 0, 0);
    private double _nowTime;
    private Timer _srvTimer;
    private int _tid;
    private List<int> _tidLst = new List<int>();
    private List<int> _recTidLst = new List<int>();

    private static readonly string LockTime = "lockTime";
    private List<PeTimeTask> _tmpTimeLst = new List<PeTimeTask>();
    private List<PeTimeTask> _taskTimeLst = new List<PeTimeTask>();
    private List<int> _tmpDelTimeLst = new List<int>();

    private int _frameCounter;

    private static readonly string LockFrame = "lockFrame";
    private List<PeFrameTask> _tmpFrameLst = new List<PeFrameTask>();
    private List<PeFrameTask> _taskFrameLst = new List<PeFrameTask>();
    private List<int> _tmpDelFrameLst = new List<int>();

    public PeTimer(int interval = 0) {
        _tidLst.Clear();
        _recTidLst.Clear();

        _tmpTimeLst.Clear();
        _taskTimeLst.Clear();

        _tmpFrameLst.Clear();
        _taskFrameLst.Clear();

        if (interval != 0) {
            _srvTimer = new Timer(interval) {
                AutoReset = true
            };

            _srvTimer.Elapsed += (object sender, ElapsedEventArgs args) => {
                Update();
            };
            _srvTimer.Start();
        }
    }

    public void Update() {
        CheckTimeTask();
        CheckFrameTask();

        DelTimeTask();
        DelFrameTask();

        if (_recTidLst.Count > 0) {
            lock (LockTid) {
                RecycleTid();
            }
        }
    }
    private void DelTimeTask() {
        if (_tmpDelTimeLst.Count > 0) {
            lock (LockTime) {
                for (int i = 0; i < _tmpDelTimeLst.Count; i++) {
                    bool isDel = false;
                    int delTid = _tmpDelTimeLst[i];
                    for (int j = 0; j < _taskTimeLst.Count; j++) {
                        PeTimeTask task = _taskTimeLst[j];
                        if (task.Tid == delTid) {
                            isDel = true;
                            _taskTimeLst.RemoveAt(j);
                            _recTidLst.Add(delTid);
                            //LogInfo("Del taskTimeLst ID:" + System.Threading.Thread.CurrentThread.ManagedThreadId.ToString());
                            break;
                        }
                    }

                    if (isDel)
                        continue;

                    for (int j = 0; j < _tmpTimeLst.Count; j++) {
                        PeTimeTask task = _tmpTimeLst[j];
                        if (task.Tid == delTid) {
                            _tmpTimeLst.RemoveAt(j);
                            _recTidLst.Add(delTid);
                            //LogInfo("Del tmpTimeLst ID:" + System.Threading.Thread.CurrentThread.ManagedThreadId.ToString());
                            break;
                        }
                    }
                }
                _tmpDelTimeLst.Clear();
            }
        }
    }
    private void DelFrameTask() {
        if (_tmpDelFrameLst.Count > 0) {
            lock (LockFrame) {
                for (int i = 0; i < _tmpDelFrameLst.Count; i++) {
                    bool isDel = false;
                    int delTid = _tmpDelFrameLst[i];
                    for (int j = 0; j < _taskFrameLst.Count; j++) {
                        PeFrameTask task = _taskFrameLst[j];
                        if (task.Tid == delTid) {
                            isDel = true;
                            _taskFrameLst.RemoveAt(j);
                            _recTidLst.Add(delTid);
                            break;
                        }
                    }

                    if (isDel)
                        continue;

                    for (int j = 0; j < _tmpFrameLst.Count; j++) {
                        PeFrameTask task = _tmpFrameLst[j];
                        if (task.Tid == delTid) {
                            _tmpFrameLst.RemoveAt(j);
                            _recTidLst.Add(delTid);
                            break;
                        }
                    }
                }
                _tmpDelFrameLst.Clear();
            }
        }
    }
    private void CheckTimeTask() {
        if (_tmpTimeLst.Count > 0) {
            lock (LockTime) {
                //加入缓存区中的定时任务
                for (int tmpIndex = 0; tmpIndex < _tmpTimeLst.Count; tmpIndex++) {
                    _taskTimeLst.Add(_tmpTimeLst[tmpIndex]);
                }
                _tmpTimeLst.Clear();
            }
        }

        //遍历检测任务是否达到条件
        _nowTime = GetUtcMilliseconds();
        for (int index = 0; index < _taskTimeLst.Count; index++) {
            PeTimeTask task = _taskTimeLst[index];
            if (_nowTime.CompareTo(task.DestTime) < 0) {
                continue;
            }
            else {
                Action<int> cb = task.Callback;
                try {
                    if (_taskHandle != null) {
                        _taskHandle(cb, task.Tid);
                    }
                    else
                    {
                        cb?.Invoke(task.Tid);
                    }
                }
                catch (Exception e) {
                    LogInfo(e.ToString());
                }

                //移除已经完成的任务
                if (task.Count == 1) {
                    _taskTimeLst.RemoveAt(index);
                    index--;
                    _recTidLst.Add(task.Tid);
                }
                else {
                    if (task.Count != 0) {
                        task.Count -= 1;
                    }
                    task.DestTime += task.Delay;
                }
            }
        }
    }
    private void CheckFrameTask() {
        if (_tmpFrameLst.Count > 0) {
            lock (LockFrame) {
                //加入缓存区中的定时任务
                for (int tmpIndex = 0; tmpIndex < _tmpFrameLst.Count; tmpIndex++) {
                    _taskFrameLst.Add(_tmpFrameLst[tmpIndex]);
                }
                _tmpFrameLst.Clear();
            }
        }

        _frameCounter += 1;
        //遍历检测任务是否达到条件
        for (int index = 0; index < _taskFrameLst.Count; index++) {
            PeFrameTask task = _taskFrameLst[index];
            if (_frameCounter < task.DestFrame) {
                continue;
            }
            else {
                Action<int> cb = task.Callback;
                try {
                    if (_taskHandle != null) {
                        _taskHandle(cb, task.Tid);
                    }
                    else
                    {
                        cb?.Invoke(task.Tid);
                    }
                }
                catch (Exception e) {
                    LogInfo(e.ToString());
                }

                //移除已经完成的任务
                if (task.Count == 1) {
                    _taskFrameLst.RemoveAt(index);
                    index--;
                    _recTidLst.Add(task.Tid);
                }
                else {
                    if (task.Count != 0) {
                        task.Count -= 1;
                    }
                    task.DestFrame += task.Delay;
                }
            }
        }
    }

    #region TimeTask
    public int AddTimeTask(Action<int> callback, double delay, PeTimeUnit timeUnit = PeTimeUnit.Millisecond, int count = 1) {
        if (timeUnit != PeTimeUnit.Millisecond) {
            switch (timeUnit) {
                case PeTimeUnit.Second:
                    delay = delay * 1000;
                    break;
                case PeTimeUnit.Minute:
                    delay = delay * 1000 * 60;
                    break;
                case PeTimeUnit.Hour:
                    delay = delay * 1000 * 60 * 60;
                    break;
                case PeTimeUnit.Day:
                    delay = delay * 1000 * 60 * 60 * 24;
                    break;
                default:
                    LogInfo("Add Task TimeUnit Type Error...");
                    break;
            }
        }
        int tid = GetTid(); ;
        _nowTime = GetUtcMilliseconds();
        lock (LockTime) {
            _tmpTimeLst.Add(new PeTimeTask(tid, callback, _nowTime + delay, delay, count));
        }
        return tid;
    }
    public void DeleteTimeTask(int tid) {
        lock (LockTime) {
            _tmpDelTimeLst.Add(tid);
            //LogInfo("TmpDel ID:" + System.Threading.Thread.CurrentThread.ManagedThreadId.ToString());
        }
        /*
         bool exist = false;

         for (int i = 0; i < taskTimeLst.Count; i++) {
             PETimeTask task = taskTimeLst[i];
             if (task.tid == tid) {
                 //taskTimeLst.RemoveAt(i);
                 for (int j = 0; j < tidLst.Count; j++) {
                     if (tidLst[j] == tid) {
                         //tidLst.RemoveAt(j);
                         break;
                     }
                 }
                 exist = true;
                 break;
             }
         }

         if (!exist) {
             for (int i = 0; i < tmpTimeLst.Count; i++) {
                 PETimeTask task = tmpTimeLst[i];
                 if (task.tid == tid) {
                     //tmpTimeLst.RemoveAt(i);
                     for (int j = 0; j < tidLst.Count; j++) {
                         if (tidLst[j] == tid) {
                             //tidLst.RemoveAt(j);
                             break;
                         }
                     }
                     exist = true;
                     break;
                 }
             }
         }

         return exist;
         */
    }
    public bool ReplaceTimeTask(int tid, Action<int> callback, float delay, PeTimeUnit timeUnit = PeTimeUnit.Millisecond, int count = 1) {
        if (timeUnit != PeTimeUnit.Millisecond) {
            switch (timeUnit) {
                case PeTimeUnit.Second:
                    delay = delay * 1000;
                    break;
                case PeTimeUnit.Minute:
                    delay = delay * 1000 * 60;
                    break;
                case PeTimeUnit.Hour:
                    delay = delay * 1000 * 60 * 60;
                    break;
                case PeTimeUnit.Day:
                    delay = delay * 1000 * 60 * 60 * 24;
                    break;
                default:
                    LogInfo("Replace Task TimeUnit Type Error...");
                    break;
            }
        }
        _nowTime = GetUtcMilliseconds();
        PeTimeTask newTask = new PeTimeTask(tid, callback, _nowTime + delay, delay, count);

        bool isRep = false;
        for (int i = 0; i < _taskTimeLst.Count; i++) {
            if (_taskTimeLst[i].Tid == tid) {
                _taskTimeLst[i] = newTask;
                isRep = true;
                break;
            }
        }

        if (!isRep) {
            for (int i = 0; i < _tmpTimeLst.Count; i++) {
                if (_tmpTimeLst[i].Tid == tid) {
                    _tmpTimeLst[i] = newTask;
                    isRep = true;
                    break;
                }
            }
        }

        return isRep;
    }
    #endregion

    #region FrameTask
    public int AddFrameTask(Action<int> callback, int delay, int count = 1) {
        int tid = GetTid();
        lock (LockTime) {
            _tmpFrameLst.Add(new PeFrameTask(tid, callback, _frameCounter + delay, delay, count));
        }
        return tid;
    }
    public void DeleteFrameTask(int tid) {
        lock (LockFrame) {
            _tmpDelFrameLst.Add(tid);
        }
        /*
        bool exist = false;

        for (int i = 0; i < taskFrameLst.Count; i++) {
            PEFrameTask task = taskFrameLst[i];
            if (task.tid == tid) {
                //taskFrameLst.RemoveAt(i);
                for (int j = 0; j < tidLst.Count; j++) {
                    if (tidLst[j] == tid) {
                        //tidLst.RemoveAt(j);
                        break;
                    }
                }
                exist = true;
                break;
            }
        }

        if (!exist) {
            for (int i = 0; i < tmpFrameLst.Count; i++) {
                PEFrameTask task = tmpFrameLst[i];
                if (task.tid == tid) {
                    //tmpFrameLst.RemoveAt(i);
                    for (int j = 0; j < tidLst.Count; j++) {
                        if (tidLst[j] == tid) {
                            //tidLst.RemoveAt(j);
                            break;
                        }
                    }
                    exist = true;
                    break;
                }
            }
        }

        return exist;
        */
    }
    public bool ReplaceFrameTask(int tid, Action<int> callback, int delay, int count = 1) {
        PeFrameTask newTask = new PeFrameTask(tid, callback, _frameCounter + delay, delay, count);

        bool isRep = false;
        for (int i = 0; i < _taskFrameLst.Count; i++) {
            if (_taskFrameLst[i].Tid == tid) {
                _taskFrameLst[i] = newTask;
                isRep = true;
                break;
            }
        }

        if (!isRep) {
            for (int i = 0; i < _tmpFrameLst.Count; i++) {
                if (_tmpFrameLst[i].Tid == tid) {
                    _tmpFrameLst[i] = newTask;
                    isRep = true;
                    break;
                }
            }
        }

        return isRep;
    }
    #endregion

    public void SetLog(Action<string> log) {
        _taskLog = log;
    }
    public void SetHandle(Action<Action<int>, int> handle) {
        _taskHandle = handle;
    }

    public void Reset() {
        _tid = 0;
        _tidLst.Clear();
        _recTidLst.Clear();

        _tmpTimeLst.Clear();
        _taskTimeLst.Clear();

        _tmpFrameLst.Clear();
        _taskFrameLst.Clear();

        _taskLog = null;
        _srvTimer.Stop();
    }

    public int GetYear() {
        return GetLocalDateTime().Year;
    }
    public int GetMonth() {
        return GetLocalDateTime().Month;
    }
    public int GetDay() {
        return GetLocalDateTime().Day;
    }
    public int GetWeek() {
        return (int)GetLocalDateTime().DayOfWeek;
    }
    public DateTime GetLocalDateTime() {
        DateTime dt = TimeZone.CurrentTimeZone.ToLocalTime(_startDateTime.AddMilliseconds(_nowTime));
        return dt;
    }
    public double GetMillisecondsTime() {
        return _nowTime;
    }
    public string GetLocalTimeStr() {
        DateTime dt = GetLocalDateTime();
        string str = GetTimeStr(dt.Hour) + ":" + GetTimeStr(dt.Minute) + ":" + GetTimeStr(dt.Second);
        return str;
    }

    #region Tool Methonds
    private int GetTid() {
        lock (LockTid) {
            _tid += 1;

            //安全代码，以防万一
            while (true) {
                if (_tid == int.MaxValue) {
                    _tid = 0;
                }

                bool used = false;
                for (int i = 0; i < _tidLst.Count; i++) {
                    if (_tid == _tidLst[i]) {
                        used = true;
                        break;
                    }
                }
                if (!used) {
                    _tidLst.Add(_tid);
                    break;
                }
                else {
                    _tid += 1;
                }
            }
        }

        return _tid;
    }
    private void RecycleTid()
    {
        foreach (var i in _recTidLst)
        {
            for (int j = 0; j < _tidLst.Count; j++) {
                if (_tidLst[j] == i) {
                    _tidLst.RemoveAt(j);
                    break;
                }
            }
        }

        _recTidLst.Clear();
    }
    private void LogInfo(string info)
    {
        _taskLog(info);
    }
    private double GetUtcMilliseconds() {
        TimeSpan ts = DateTime.UtcNow - _startDateTime;
        return ts.TotalMilliseconds;
    }
    private string GetTimeStr(int time) {
        if (time < 10) {
            return "0" + time;
        }
        else {
            return time.ToString();
        }
    }
    #endregion

    class PeTimeTask {
        public int Tid;
        public Action<int> Callback;
        public double DestTime;//单位：毫秒
        public double Delay;
        public int Count;

        public PeTimeTask(int tid, Action<int> callback, double destTime, double delay, int count) {
            this.Tid = tid;
            this.Callback = callback;
            this.DestTime = destTime;
            this.Delay = delay;
            this.Count = count;
        }
    }

    class PeFrameTask {
        public int Tid;
        public Action<int> Callback;
        public int DestFrame;
        public int Delay;
        public int Count;

        public PeFrameTask(int tid, Action<int> callback, int destFrame, int delay, int count) {
            this.Tid = tid;
            this.Callback = callback;
            this.DestFrame = destFrame;
            this.Delay = delay;
            this.Count = count;
        }
    }
}

public enum PeTimeUnit {
    Millisecond,
    Second,
    Minute,
    Hour,
    Day
}
