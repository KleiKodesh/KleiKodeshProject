using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace DocSeferLib.Helpers
{
    public class ScreenFreeze : IDisposable
    {
        public ScreenFreeze() 
        {
            Vsto.Application.ScreenUpdating = false;
        }
        public void Dispose()
        {
            Vsto.Application.ScreenUpdating = true;
        }
    }
}
