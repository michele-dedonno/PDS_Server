using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Server
{
    class UnexpectedLoginException:Exception
    {
        public UnexpectedLoginException(string message) : base(message)
        {
        }
    }
}
