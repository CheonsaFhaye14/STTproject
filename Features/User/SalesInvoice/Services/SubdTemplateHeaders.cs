using STTproject.Data;

namespace STTproject.Features.User.SalesInvoice.Services
{
	public class SubdTemplateHeaders
	{
		public static IReadOnlyDictionary<string, string[]> GetTemplateAliases(SubDistributor subDistributor)
		{
			var map = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase);

			if (subDistributor == null)
				return map;

			if (IsStarwideDistribution(subDistributor))
			{
				map["InvoiceCode"] = new[] { "lst_si" };
				map["InvoiceDate"] = new[] { "lst_date" };
				map["CustomerCode"] = new[] { "lst_cust1" };
				map["CustomerName"] = new[] { "lst_cust2" };
				map["NetAmount"] = new[] { "lst_net2" };
				map["SkuCode"] = new[] { "lst_head1" };
				map["ItemName"] = new[] { "lst_desc" };
				map["SalesManName"] = new[] { "lst_agent2" };
				map["CaseQuantity"] = new[] { "lst_qnty1" };
				map["PieceQuantity"] = new[] { "lst_qnty2" };
				map["AddressLine"] = new[] { "lst_addr" };
				map["Province"] = new[] { "1st_town" };
				map["CustomerType"] = new[] { "lst_trade" };
				return map;
			}

			if (IsVarleyCorp(subDistributor))
			{
				map["InvoiceCode"] = new[] { "so_number" };
				map["InvoiceDate"] = new[] { "so_date" };
				map["CustomerCode"] = new[] { "customer_code" };
				map["CustomerName"] = new[] { "customer_name" };
				map["AddressLine"] = new[] { "address" };
				map["CityMunicipality"] = new[] { "city" };
				map["Province"] = new[] { "province" };
				map["NetAmount"] = new[] { "net_amount" };
				map["SkuCode"] = new[] { "item_number" };
				map["SalesManName"] = new[] { "salesman_name" };
				map["CaseQuantity"] = new[] { "case_total" };
				map["DozenQuantity"] = new[] { "dozen_total" };
				map["PieceQuantity"] = new[] { "pieces_total" };
				return map;
			}
			if (IsRiteBeacon(subDistributor))
			{
				map["InvoiceCode"] = new[] { "ref.no." };
				map["InvoiceDate"] = new[] { "date" };
				map["CustomerCode"] = new[] { "cust./supp." };
				map["CustomerName"] = new[] { "name" };
				map["Province"] = new[] { "address 3" };
				map["CityMunicipality"] = new[] { "address 2" };
				map["AddressLine"] = new[] { "address 1" };
				map["SkuCode"] = new[] { "item no." };
				map["SalesManName"] = new[] { "agent name" };
				map["Quantity"] = new[] { "qty" };
				map["UnitOfMeasure"] = new[] { "uom" };
				map["OrderType"] = new[] { "type" };
				return map;
			}
			if (IsMegaPanay(subDistributor))
			{
				map["InvoiceCode"] = new[] { "inv" };
				map["InvoiceDate"] = new[] { "date" };
				map["CustomerName"] = new[] { "acc name" };
				map["SalesManName"] = new[] { "ads" };
				map["SkuCode"] = new[] { "item id" };
				map["PieceQuantity"] = new[] { "piece" };
				map["CaseQuantity"] = new[] { "case" };
				return map;
			}
			if (IsFDRPremier(subDistributor))
			{
				map["InvoiceCode"] = new[] { "reference" };
				map["InvoiceDate"] = new[] { "date" };
				map["CustomerName"] = new[] { "customer" };
				map["NetAmount"] = new[] { "netsales" };
				map["SkuCode"] = new[] { "code" };
				map["SalesManName"] = new[] { "salesrep" };
				map["CaseQuantity"] = new[] { "qtycs" };
				map["InBoxQuantity"] = new[] { "qtyib" };
				map["PieceQuantity"] = new[] { "qtypc" };
				return map;
			}
			if (isGranvilleIlocos(subDistributor))
			{
				map["InvoiceCode"] = new[] { "so_number" };
				map["InvoiceDate"] = new[] { "invoice_date" };
				map["CustomerName"] = new[] { "customer_name" };
				map["SkuCode"] = new[] { "item_code" };
				map["ItemName"] = new[] { "item_description" };
				map["Quantity"] = new[] { "qty" };
				map["UnitofMeasure"] = new[] { "unit" };
				map["SalesManName"] = new[] { "salesrep" };
				map["Province"] = new[] { "province" };
				map["CityMunicipality"] = new[] { "town_or_city" };
				return map;
			}
			if (isValleyWide(subDistributor))
			{

				map["InvoiceCode"] = new[] { "invoice no" };
				map["InvoiceDate"] = new[] { "invoice date" };
				map["CustomerName"] = new[] { "customer" };
				map["OrderType"] = new[] { "trx type" };
				map["SalesManName"] = new[] { "salesrep" };
				map["SkuCode"] = new[] { "item code" };
				map["Quantity"] = new[] { "qty" };
				map["UnitofMeasure"] = new[] { "unit" };
				return map;
			}
			if (isGranvilleDagupan(subDistributor) || isGranvilleLaUnion(subDistributor) || isGranvilleBaguio(subDistributor))
			{
				map["InvoiceCode"] = new[] { "invoice" };
				map["InvoiceDate"] = new[] { "invoicedate" };
				map["CustomerCode"] = new[] { "customer_id" };
				map["CustomerName"] = new[] { "customername" };
				map["CustomerType"] = new[] { "subcategory" };
				map["OrderType"] = new[] { "trxtype" };
				map["SkuCode"] = new[] { "item_id" };
				map["ItemName"] = new[] { "product" };
				map["SalesManName"] = new[] { "salesrep" };
				map["PieceQuantity"] = new[] { "item_qty_piece" };
				map["Province"] = new[] { "province" };
				map["CityMunicipality"] = new[] { "town_or_city" };
				map["AddressLine"] = new[] { "address1" };
				map["FreeItems"] = new[] { "gross" };
				return map;
			} 
			
			if (isNEMarketing(subDistributor))
			{
				map["InvoiceCode"] = new[] { "invoice no" };
				map["InvoiceDate"] = new[] { "invoice date" };
				map["CustomerCode"] = new[] { "so number" };
				map["CustomerName"] = new[] { "customer" };
				map["SalesManName"] = new[] { "salesrep" };
				map["SkuCode"] = new[] { "item code" };
				map["Quantity"] = new[] { "qty" };
				map["UnitofMeasure"] = new[] { "unit" };
				return map;
			}
			if (isPadsaCorp(subDistributor))
			{
				map["InvoiceCode"] = new[] { "num" };
				map["InvoiceDate"] = new[] { "date" };
				map["CustomerCode"] = new[] { "customer code" };
				map["CustomerName"] = new[] { "name" };
				map["CustomerType"] = new[] { "channel" };
				map["SalesManName"] = new[] { "rep" };
				map["Province"] = new[] { "province" };
				map["CityMunicipality"] = new[] { "town" };
				map["AddressLine"] = new[] { "address" };
				map["SkuCode"] = new[] { "item" };
				map["ItemName"] = new[] { "item description" };
				map["Quantity"] = new[] { "qty" };
				map["UnitofMeasure"] = new[] { "u/m" };
				return map;
			}
			if (isParfaitLaguna(subDistributor) || isParfaitQuezon(subDistributor))
			{
				map["InvoiceCode"] = new[] { "ref.no." };
				map["InvoiceDate"] = new[] { "date" };
				map["CustomerCode"] = new[] { "cust./supp." };
				map["CustomerName"] = new[] { "name" };
				map["SalesManName"] = new[] { "agent" };
				map["SkuCode"] = new[] { "item no." };
				map["Quantity"] = new[] { "qty" };
				map["OrderType"] = new[] { "type" };
				map["Province"] = new[] { "area" };
				return map;
			}
			if (isMaranathaSales(subDistributor))
			{
				map["InvoiceCode"] = new[] { "invoice#" };
				map["InvoiceDate"] = new[] { "date" };
				map["CustomerName"] = new[] { "report name" };
				map["SalesManName"] = new[] { "salesman" };
				map["Item Name"] = new[] { "product" };
				map["PieceQuantity"] = new[] { "qty pc" };
				map["NetAmount"] = new[] { "amount" };
				map["Province"] = new[] { "province" };
				map["CityMunicipality"] = new[] { "area" };
				return map;
			}
			if (isVerConMarketing(subDistributor))
			{
				map["InvoiceCode"] = new[] { "invoice" };
				map["InvoiceDate"] = new[] { "date" };
				map["CustomerName"] = new[] { "account name" };
				map["SalesManName"] = new[] { "salesman" };
				map["SkuCode"] = new[] { "item code" };
				map["PieceQuantity"] = new[] { "piece" };
				map["CaseQuantity"] = new[] { "case" };
				return map;
			}
			if (isCaragaMultilines(subDistributor))
			{
				map["InvoiceCode"] = new[] { "ref. #" };
				map["InvoiceDate"] = new[] { "date" };
				map["CustomerCode"] = new[] { "cust. #" };
				map["CustomerName"] = new[] { "customer" };
				map["SalesManName"] = new[] { "sales rep" };
				map["OrderType"] = new[] { "type" };
				map["SkuCode"] = new[] { "code" };
				map["PieceQuantity"] = new[] { "pcs" };
				map["CaseQuantity"] = new[] { "case" };
				map["NetAmount"] = new[] { "amount" };
				map["FreeItems"] = new[] { "unit price" };
				return map;
			}
			if (isZCFirstStepConsumerMarketing(subDistributor))
			{
				map["InvoiceCode"] = new[] { "os_no" };
				map["InvoiceDate"] = new[] { "d_date" };
				map["CustomerCode"] = new[] { "customer_code" };
				map["CustomerName"] = new[] { "customer_name" };
				map["SalesManName"] = new[] { "sales_man" };
				map["SkuCode"] = new[] { "stock_no" };
				map["Quantity"] = new[] { "um_qty" };
				map["UnitofMeasure"] = new[] { "um" };
				return map;
			}
			if (isGYTrading(subDistributor))
			{
				map["InvoiceCode"] = new[] { "dr #" };
				map["InvoiceDate"] = new[] { "p.o date" };
				map["CustomerName"] = new[] { "outlet name" };
				map["CityMunicipality"] = new[] { "outlet add" };
				map["SalesManName"] = new[] { "salesman" };
				map["ItemName"] = new[] { "sku description" };
				map["UnitofMeasure"] = new[] { "units" };
				map["Quantity"] = new[] { "qty" };
				return map;
			}
			if (isPeaseCorp(subDistributor))
			{
				map["InvoiceCode"] = new[] { "ref_no" };
				map["InvoiceDate"] = new[] { "ref_date" };
				map["CustomerCode"] = new[] { "customer_code" };
				map["CustomerName"] = new[] { "customer_name" };
				map["CustomerType"] = new[] { "customer_group" };
				map["SalesManName"] = new[] { "salesman_name" };
				map["SkuCode"] = new[] { " item_code " };
				map["ItemName"] = new[] { " item_desc " };
				map["Quantity"] = new[] { "qty" };
				map["UnitofMeasure"] = new[] { "symbol" };
				return map;
			}
			if (isKingAltonBatangas(subDistributor))
			{
				map["InvoiceCode"] = new[] { "inv nbr" };
				map["InvoiceDate"] = new[] { "inv date" };
				map["CustomerCode"] = new[] { "custid" };
				map["CustomerName"] = new[] { "customer name" };
				map["CustomerType"] = new[] { "trade" };
				map["CityMunicipality"] = new[] { "town" };
				map["AddressLine"] = new[] { "address" };
				map["SalesManName"] = new[] { "agent" };
				map["SkuCode"] = new[] { "skuid" };
				map["ItemName"] = new[] { "sku" };
				map["PieceQuantity"] = new[] { "qty (pcs)" };
				map["CaseQuantity"] = new[] { "qty (cs)" };
				map["FreeItems"] = new[] { "amount" };
				return map;
			}
			if (isApexFoods(subDistributor))
			{
				map["InvoiceCode"] = new[] { "Doc No." };
				map["InvoiceDate"] = new[] { "date" };
				map["CustomerCode"] = new[] { "customer code" };
				map["CustomerName"] = new[] { "name" };
				map["SalesManName"] = new[] { "sales rep" };
				// Item No/Name = 4806527480082 PERLA LAU BAR 110G X 144 BLUEx 144 / Pringles Snack Cheese 12 x 102gx 12
				map["SkuCode"] = new[] { "item code" };
				map["ItemName"] = new[] { "product desc" };
				map["CaseQuantity"] = new[] { "Qty Invoiced (in cases)" };
				map["UnitofMeasure"] = new[] { "UOM" }; //CS ( 576 Pcs )
				// Qty Invoiced (in Cases) * Config == pc quantity
				map["Quantity"] = new[] { "quantity" };

				return map;
			}
			return map;
		}

		public static IReadOnlyDictionary<string, string[]> GetGlobalAliases()
		{
			var map = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase)
			{
				["InvoiceCode"] = new[]
				{
					"invoicecode", "invoice code", "invcode", "inv code", "so number", "so no", "sales order number", "sales order no", "dr number", "dr no", "reference", "ref no", "ref. #", "num", "os_no"
				},
				["InvoiceDate"] = new[]
				{
					"invoicedate", "invoice date", "invdate", "inv date", "so date", "sales order date", "dr date", "p.o date", "date-no.", "d_date"
				},
				["CustomerCode"] = new[]
				{
					"customercode", "customer code", "custcode", "cust code", "customer#", "cust.#","so number","so no","sales order number","sales order no","customer #","cust. #","customer_code"
				},
				["CustomerName"] = new[]
				{
					"customername","customer name","custname","cust name","account name","outlet name","customer","name", "customer (code or name)"
				},
				["AddressLine"] = new[]
				{
					"addressline","address line","address"
				},
				["CityMunicipality"] = new[]
				{
					"citymunicipality","city municipality","town_or_city","town","area"
				},
				["Province"] = new[]
				{
					"province"
				},
				["NetAmount"] = new[]
				{
					"netamount","net amount","netsales","amount"
				},
				["SalesManName"] = new[]
				{
					"salesmanname","salesman name","salesrep","agent","ads","pic name"
				},
				["SkuCode"] = new[]
				{
					"skucode","sku code","itemcode","item code","item_number","code"
				},
				["UnitOfMeasure"] = new[]
				{
					"uom","unitofmeasure","unit of measure","um","u/m","unit"
				},
				["Quantity"] = new[]
				{
					"quantity","qty","qnty","qty pcs","qty piece","um_qty"
				},
				["CaseQuantity"] = new[]
				{
					"casequantity","case quantity","case_total","case"
				},
				["InBoxQuantity"] = new[]
				{
					"inboxquantity","inbox quantity","qtyib"
				},
				["DozenQuantity"] = new[]
				{
					"dozenquantity","dozen quantity","dozen_total","dozen"
				},
				["PieceQuantity"] = new[]
				{
					"piecequantity","piece quantity","pieces_total","pieces","pcs","piece"
				},
				["OrderType"] = new[]
				{
					"ordertype","order type","trxtype","trx type","type"
				},
				["ItemName"] = new[]
				{
					"skuname", "sku name", "itemname", "item name", "product", "item description", "item name (spec)", "item (sku or name)"
				}
			};

			return map;
		}

		private static bool IsStarwideDistribution(SubDistributor subDistributor)
		{
			return string.Equals(subDistributor.SubdName?.Trim(), "STARWIDE DISTRIBUTION", StringComparison.OrdinalIgnoreCase)
				|| string.Equals(subDistributor.SubdCode?.Trim(), "01GMA06", StringComparison.OrdinalIgnoreCase);
		}

		private static bool IsVarleyCorp(SubDistributor subDistributor)
		{
			return string.Equals(subDistributor.SubdCode?.Trim(), "01GMA08", StringComparison.OrdinalIgnoreCase)
				|| string.Equals(subDistributor.SubdName?.Trim(), "VARLEY CORP", StringComparison.OrdinalIgnoreCase);
		}
		private static bool IsRiteBeacon(SubDistributor subDistributor)
		{
			return string.Equals(subDistributor.SubdCode?.Trim(), "01GMA07", StringComparison.OrdinalIgnoreCase)
				|| string.Equals(subDistributor.SubdName?.Trim(), "RITE BEACON MARKETING", StringComparison.OrdinalIgnoreCase);
		}
		private static bool IsFDRPremier(SubDistributor subDistributor)
		{
			return string.Equals(subDistributor.SubdCode?.Trim(), "04VIS06", StringComparison.OrdinalIgnoreCase)
				|| string.Equals(subDistributor.SubdName?.Trim(), "FDR PREMIER TRADING", StringComparison.OrdinalIgnoreCase);
		}
		private static bool IsMegaPanay(SubDistributor subDistributor)
		{
			return string.Equals(subDistributor.SubdCode?.Trim(), "04VIS04", StringComparison.OrdinalIgnoreCase)
				|| string.Equals(subDistributor.SubdName?.Trim(), "MEGA PANAY DISTRIBUTION INC.", StringComparison.OrdinalIgnoreCase);
		}
		private static bool isGranvilleIlocos(SubDistributor subDistributor)
		{
			return string.Equals(subDistributor.SubdCode?.Trim(), "02NCL01", StringComparison.OrdinalIgnoreCase)
				|| string.Equals(subDistributor.SubdName?.Trim(), "GRANVILLE SUPPLY CHAIN MGMT - ILOCOS", StringComparison.OrdinalIgnoreCase);
		}
		private static bool isGranvilleBaguio(SubDistributor subDistributor)
		{
			return string.Equals(subDistributor.SubdCode?.Trim(), "02NCL02", StringComparison.OrdinalIgnoreCase)
				|| string.Equals(subDistributor.SubdName?.Trim(), "GRANVILLE SUPPLY CHAIN MGMT-BAGUIO", StringComparison.OrdinalIgnoreCase);
		}
		private static bool isValleyWide(SubDistributor subDistributor)
		{
			return string.Equals(subDistributor.SubdCode?.Trim(), "02NCL04", StringComparison.OrdinalIgnoreCase)
				|| string.Equals(subDistributor.SubdName?.Trim(), "VALLEY WIDE SALES AND DISTRIBUTION", StringComparison.OrdinalIgnoreCase);
		}
		private static bool isGranvilleDagupan(SubDistributor subDistributor)
		{
			return string.Equals(subDistributor.SubdCode?.Trim(), "02NCL09", StringComparison.OrdinalIgnoreCase)
				|| string.Equals(subDistributor.SubdName?.Trim(), "GRANVILLE SUPPLY CHAIN MGMT-DAGUPAN", StringComparison.OrdinalIgnoreCase);
		}
		private static bool isGranvilleLaUnion(SubDistributor subDistributor)
		{
			return string.Equals(subDistributor.SubdCode?.Trim(), "02NCL02-1", StringComparison.OrdinalIgnoreCase)
				|| string.Equals(subDistributor.SubdName?.Trim(), "GRANVILLE SUPPLY CHAIN MGMT- LA UNION", StringComparison.OrdinalIgnoreCase);
		}
		private static bool isNEMarketing(SubDistributor subDistributor)
		{
			return string.Equals(subDistributor.SubdCode?.Trim(), "02NCL11", StringComparison.OrdinalIgnoreCase)
				|| string.Equals(subDistributor.SubdName?.Trim(), "N E MARKETING", StringComparison.OrdinalIgnoreCase);
		}
		private static bool isPadsaCorp(SubDistributor subDistributor)
		{
			return string.Equals(subDistributor.SubdCode?.Trim(), "02NCL14", StringComparison.OrdinalIgnoreCase)
				|| string.Equals(subDistributor.SubdName?.Trim(), "PADSA ENTERPRISES CORPORATION", StringComparison.OrdinalIgnoreCase);
		}
		private static bool isParfaitLaguna(SubDistributor subDistributor)
		{
			return string.Equals(subDistributor.SubdCode?.Trim(), "03SL02", StringComparison.OrdinalIgnoreCase)
				|| string.Equals(subDistributor.SubdName?.Trim(), "PARFAIT DIRECTE ENERGISANT MKTG.- LAGUNA", StringComparison.OrdinalIgnoreCase);
		}
		private static bool isParfaitQuezon(SubDistributor subDistributor)
		{
			return string.Equals(subDistributor.SubdCode?.Trim(), "03SL03", StringComparison.OrdinalIgnoreCase)
				|| string.Equals(subDistributor.SubdName?.Trim(), "PARFAIT DIRECTE ENERGISANT MKTG.-QUEZON", StringComparison.OrdinalIgnoreCase);
		}
		private static bool isMaranathaSales(SubDistributor subDistributor)
		{
			return string.Equals(subDistributor.SubdCode?.Trim(), "03SL05", StringComparison.OrdinalIgnoreCase)
				|| string.Equals(subDistributor.SubdName?.Trim(), "MARANATHA SALES DISTRIBUTOR", StringComparison.OrdinalIgnoreCase);
		}
		private static bool isVerConMarketing(SubDistributor subDistributor)
		{
			return string.Equals(subDistributor.SubdCode?.Trim(), "04VIS03", StringComparison.OrdinalIgnoreCase)
				|| string.Equals(subDistributor.SubdName?.Trim(), "VER-CON MARKETING INC.", StringComparison.OrdinalIgnoreCase);
		}
		private static bool isCaragaMultilines(SubDistributor subDistributor)
		{
			return string.Equals(subDistributor.SubdCode?.Trim(), "05MIN02", StringComparison.OrdinalIgnoreCase)
				|| string.Equals(subDistributor.SubdName?.Trim(), "CARAGA MULTILINES DISTRIBUTION INC", StringComparison.OrdinalIgnoreCase);
		}
		private static bool isZCFirstStepConsumerMarketing(SubDistributor subDistributor)
		{
			return string.Equals(subDistributor.SubdCode?.Trim(), "05MIN04", StringComparison.OrdinalIgnoreCase)
				|| string.Equals(subDistributor.SubdName?.Trim(), "Z.C. FIRST STEP CONSUMER MARKETING", StringComparison.OrdinalIgnoreCase);
		}
		private static bool isGYTrading(SubDistributor subDistributor)
		{
			return string.Equals(subDistributor.SubdCode?.Trim(), "05MIN06", StringComparison.OrdinalIgnoreCase)
				|| string.Equals(subDistributor.SubdName?.Trim(), "G.Y. TRADING", StringComparison.OrdinalIgnoreCase);
		}
		private static bool isPeaseCorp(SubDistributor subDistributor)
		{
			return string.Equals(subDistributor.SubdCode?.Trim(), "05MIN11", StringComparison.OrdinalIgnoreCase)
				|| string.Equals(subDistributor.SubdName?.Trim(), "PEASE CORP.", StringComparison.OrdinalIgnoreCase);
		}
		private static bool isKingAltonBatangas(SubDistributor subDistributor)
		{
			return string.Equals(subDistributor.SubdCode?.Trim(), "03SL04", StringComparison.OrdinalIgnoreCase)
				|| string.Equals(subDistributor.SubdName?.Trim(), "KING ALTON BATANGAS", StringComparison.OrdinalIgnoreCase);
		}
		private static bool isApexFoods(SubDistributor subDistributor)
		{
			return string.Equals(subDistributor.SubdCode?.Trim(), "04VIS07", StringComparison.OrdinalIgnoreCase)
				|| string.Equals(subDistributor.SubdName?.Trim(), "APEX FOODS DISTRIBUTION CORP.", StringComparison.OrdinalIgnoreCase);
		}
	}
}