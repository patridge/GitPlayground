using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace SubmoduleUpdateGenerator
{
    /// <summary>
    /// Represents the minimum manditory keys (per https://git-scm.com/docs/gitmodules.html).
    /// </summary>
    public class GitSubmoduleEntry
    {
        public string Name { get; set; }
        public string Path { get; set; }
        public string Url { get; set; }
        // branch
        // ?update
        // ?fetchRecurseSubmodules
        // ?ignore
        // ?shallow
    }
}
