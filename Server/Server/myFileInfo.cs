using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Server
{
    public class myFileInfo
    {
        private string name;
        private string relativePath;
        private string checksum;
        private long size;
        private string timeStamp;

        public string TimeStamp
        {
            get
            {
                return timeStamp;
            }

            set
            {
                timeStamp = value;
            }
        }

        public string Name
        {
            get
            {
                return name;
            }

            set
            {
                name = value;
            }
        }

        public string RelativePath
        {
            get
            {
                return relativePath;
            }

            set
            {
                relativePath = value;
            }
        }

        public string Checksum
        {
            get
            {
                return checksum;
            }

            set
            {
                checksum = value;
            }
        }

        public long Size
        {
            get
            {
                return size;
            }

            set
            {
                size = value;
            }
        }
    }
}
