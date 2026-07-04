using LexiFlow.Views;

namespace LexiFlow
{
    public partial class AppShell : Shell
    {
        public AppShell()
        {
            InitializeComponent();
            // Pages reached by navigation (not tabs) must have their routes registered.
            Routing.RegisterRoute(nameof(TestGrammarView), typeof(TestGrammarView));
        }
    }
}
