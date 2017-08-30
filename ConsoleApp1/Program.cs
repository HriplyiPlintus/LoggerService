using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ConsoleApp1
{
	class Program
	{
		static void Main(string[] args)
		{
			maintable entity = new maintable
			{
				message = "asdf",
				source = "asdf",
				role = "asdf",
				point = "asdf",
				timestamp = DateTime.Now,
				username = "asdf"
			};

			using(var db = new CSPAContext())
			{
				db.maintables.Add(entity);
				db.SaveChanges();
			}
		}
	}
}
