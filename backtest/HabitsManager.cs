using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.Serialization.Formatters.Binary;

public class HabitsManager
{
    private const string HabitsFile = "habits.dat"; // Fichier pour la liste des habitudes
    private string DailyFile => $"{DateTime.Now:yyyy-MM-dd}.dat"; // Fichier journalier

    public List<Habit> Habits { get; private set; } = new List<Habit>();
    public List<HabitState> DailyHabitStates { get; private set; } = new List<HabitState>();

    public HabitsManager()
    {
        LoadHabits();
        LoadDailyHabits();
    }

    public void LoadHabits()
    {
        if (File.Exists(HabitsFile))
        {
            using (var stream = File.OpenRead(HabitsFile))
            {
                var formatter = new BinaryFormatter();
                Habits = (List<Habit>)formatter.Deserialize(stream);
            }
        }
    }
    public void RemoveHabit(string habitName)
    {
        // Recherche de l'habitude par son nom
        var habitToRemove = Habits.FirstOrDefault(h => h.Name == habitName);

        if (habitToRemove != null)
        {
            // Suppression de l'habitude
            Habits.Remove(habitToRemove);
            // Suppression des états journaliers liés à cette habitude
            DailyHabitStates.RemoveAll(h => h.HabitName == habitName);

            // Sauvegarde des modifications
            SaveHabits();
            SaveDailyHabits();
        }
    }


    public void SaveHabits()
    {
        using (var stream = File.OpenWrite(HabitsFile))
        {
            var formatter = new BinaryFormatter();
            formatter.Serialize(stream, Habits);
        }
    }

    public void LoadDailyHabits()
    {
        if (File.Exists(DailyFile))
        {
            using (var stream = File.OpenRead(DailyFile))
            {
                var formatter = new BinaryFormatter();
                DailyHabitStates = (List<HabitState>)formatter.Deserialize(stream);
            }
        }
        else
        {
            // Initialiser avec toutes les habitudes non cochées
            DailyHabitStates = Habits.Select(h => new HabitState(h.Name, false)).ToList();
        }
    }

    public void SaveDailyHabits()
    {
        using (var stream = File.OpenWrite(DailyFile))
        {
            var formatter = new BinaryFormatter();
            formatter.Serialize(stream, DailyHabitStates);
        }
    }

    public void AddHabit(string habitName)
    {
        if (!string.IsNullOrWhiteSpace(habitName))
        {
            Habits.Add(new Habit(habitName));
            SaveHabits();

            DailyHabitStates.Add(new HabitState(habitName, false));
            SaveDailyHabits();
        }
    }

    public void UpdateHabitState(string habitName, bool isChecked)
    {
        var habitState = DailyHabitStates.FirstOrDefault(h => h.HabitName == habitName);
        if (habitState != null)
        {
            habitState.IsChecked = isChecked;
            SaveDailyHabits();
        }
    }
}

[Serializable]
public class Habit
{
    public string Name { get; set; }

    public Habit(string name)
    {
        Name = name;
    }
}

[Serializable]
public class HabitState
{
    public string HabitName { get; set; }
    public bool IsChecked { get; set; }

    public HabitState(string habitName, bool isChecked)
    {
        HabitName = habitName;
        IsChecked = isChecked;
    }
}
public class PerformanceStat
{
    public double PercentTP { get; set; }
    public double PercentSL { get; set; }

    public PerformanceStat()
    {
        PercentTP = 0;
        PercentSL = 0;
    }
}
