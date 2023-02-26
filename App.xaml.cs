using System.Windows;

namespace holey
{
    /// <summary>
    /// Interaction logic for App.xaml
    /// </summary>
    public partial class App : Application
    {
        private void App_OnStartup(object sender, StartupEventArgs e)
        {
            if (e.Args.Length == 0)
            {
                new PetWindow().Show();
            }
            else
                foreach (var t in e.Args)
                {
                    if (t.Equals("main"))
                    {
                        new MainWindow().Show();
                        break;
                    }
                    else if (t.Equals("ProgressSuspend"))
                    {
                        PetWindow pet = new PetWindow();
                        pet.Show();
                        pet.Suspend();
                    }
                    else if (t.Equals("ProgressResume"))
                    {
                        PetWindow pet = new PetWindow();
                        pet.Show();
                        pet.Resume();
                    }
                }
        }
    }
}