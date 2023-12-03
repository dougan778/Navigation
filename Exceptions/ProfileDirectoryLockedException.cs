using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Navigation.Exceptions
{
    public class ProfileDirectoryLockedException : System.Exception
    {
        public ProfileDirectoryLockedException(string message, Exception inner = null) : base(message, inner) { }
    }
}
