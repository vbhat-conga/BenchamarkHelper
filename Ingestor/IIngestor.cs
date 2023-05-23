using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace BenchamarkHelper.Ingestor
{
    public interface IIngestor
    {
        Task IngestData(DirectoryInfo directory);
    }
}
