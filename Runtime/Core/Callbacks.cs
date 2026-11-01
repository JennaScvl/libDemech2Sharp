using System.Collections.Generic;

namespace Demech2
{
    // A timed callback (demech2 MW2/include/callbacks.h, MW2/src/callbacks.c): its function runs every m_period ticks
    // of the clock, with event 0 once when it is made and 1 each time it is due; returning 0 removes it.
    public abstract class TimedCallback
    {
        public int m_period;
        public int m_nextTime;
        public int m_lastTime;

        // TimedCallbackFn (m_fn): p_event 0 (p_data is the task's data) and 1.
        public abstract bool Run(int p_event, string p_data, int p_clock, int p_period);
    }

    // A list of timed callbacks: new ones go at the head (CreateDetachedTask), and RunTimedCallbacks walks it in order.
    public sealed class TimedCallbackList
    {
        readonly List<TimedCallback> tasks = new List<TimedCallback>();

        // CreateDetachedTask: adds p_task at the head and runs its event 0; drops it if that fails.
        public TimedCallback CreateDetachedTask(TimedCallback p_task, int p_period, string p_data)
        {
            p_task.m_period = p_period;
            p_task.m_lastTime = Clock.g_currentClock;
            p_task.m_nextTime = p_task.m_lastTime;
            tasks.Insert(0, p_task);
            if (!p_task.Run(0, p_data, Clock.g_currentClock, p_period))
            {
                tasks.Remove(p_task);
                return null;
            }

            return p_task;
        }

        public void RunTimedCallbacks()
        {
            int clock = Clock.g_currentClock;
            for (int i = 0; i < tasks.Count; i++)
            {
                TimedCallback task = tasks[i];
                if (task.m_nextTime <= clock)
                {
                    task.m_nextTime = task.m_period + clock;
                    if (!task.Run(1, null, clock, task.m_period))
                    {
                        tasks.RemoveAt(i);
                        i--;
                    }
                    else
                    {
                        task.m_lastTime = clock;
                    }
                }
            }
        }

        public void Clear() => tasks.Clear();

        // RemoveAllTasks: each task told it's going (event 2), then gone.
        public void RemoveAllTasks()
        {
            for (int i = 0; i < tasks.Count; i++) tasks[i].Run(2, null, Clock.g_currentClock, tasks[i].m_period);
            tasks.Clear();
        }

        // SignalAllTasks: each task told its object is back (event -1).
        public void SignalAllTasks()
        {
            for (int i = 0; i < tasks.Count; i++) tasks[i].Run(-1, null, Clock.g_currentClock, tasks[i].m_period);
        }
    }
}
