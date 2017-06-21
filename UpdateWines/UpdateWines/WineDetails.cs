using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace UpdateWines
{
    public class WineDetails
    {
        //private int wineId;
        private string wineName;
        private string vintage;
        private int store;
		private string Barcode;

		public string BarCode
		{
			get { return this.Barcode; }
			set { this.Barcode = value; }
		}
		//public int WineId
  //      {
  //          get { return this.wineId; }
  //          set { this.wineId = value; }
  //      }
        public string WineName
        {
            get { return this.wineName; }
            set { this.wineName = value; }
        }
        public string Vintage
        {
            get { return this.vintage; }
            set { this.vintage = value; }
        }
        public int Store
        {
            get { return this.store; }
            set { this.store = value; }
        }
    }
}
