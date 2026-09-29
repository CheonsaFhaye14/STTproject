using System;
using System.Collections.Generic;

namespace STTproject.Data;

public partial class SubdSellout
{
    public long SdsId { get; set; }

    public bool WithEncoder { get; set; }

    public string Real { get; set; } = null!;

    public string SubD { get; set; } = null!;

    public string Lookup { get; set; } = null!;

    public int? LineCount { get; set; }

    public string OrdNo { get; set; } = null!;

    public string OrigOrdType { get; set; } = null!;

    public DateOnly EnterDate { get; set; }

    public string CusNo { get; set; } = null!;

    public string BillToName { get; set; } = null!;

    public string ShipToName { get; set; } = null!;

    public string? ShipInstruction1 { get; set; }

    public string? ShipInstruction2 { get; set; }

    public string? ShipViaCd { get; set; }

    public string? ArTermsCd { get; set; }

    public string? Brand0001 { get; set; }

    public string? ItemNo { get; set; }

    public string SageItemNo { get; set; } = null!;

    public string SageItemDesc { get; set; } = null!;

    public string ItemDescription { get; set; } = null!;

    public string? Loc { get; set; }

    public decimal QtyOrdered { get; set; }

    public string QtyToShip { get; set; } = null!;

    public decimal? QtyReturnToStk { get; set; }

    public string Salesperson { get; set; } = null!;

    public string? Slm { get; set; }

    public string? CusTypeCd { get; set; }

    public string? CusTypeDesc { get; set; }

    public string CustTypeDescRevised { get; set; } = null!;

    public string CustTypeDescRevisedUsed { get; set; } = null!;

    public string? Trim { get; set; }

    public string Principal2 { get; set; } = null!;

    public string? Principal { get; set; }

    public string? Brand { get; set; }

    public decimal? PriceWithVat { get; set; }

    public decimal? ItemWeight { get; set; }

    public short Year { get; set; }

    public string CalendarPeriod { get; set; } = null!;

    public string DbsCalPeriod { get; set; } = null!;

    public string Cal { get; set; } = null!;

    public decimal AmountWithVat { get; set; }

    public decimal AmountWoVat { get; set; }

    public decimal? AmountDiscount { get; set; }

    public bool? Uba { get; set; }

    public bool SalePlus { get; set; }

    public string? QuantitySellingUnit { get; set; }

    public string? PeriodPrin { get; set; }

    public string? Country { get; set; }

    public string? SlspsnNo { get; set; }

    public string? SlspsnName { get; set; }

    public string? SlspsnNo2 { get; set; }

    public DateOnly OehhDate { get; set; }

    public byte WeekCalendar { get; set; }

    public string? ShipToAddress1 { get; set; }

    public string? ShipToAddress2 { get; set; }

    public string? ShipToAddress3 { get; set; }

    public string? ShipToCountry { get; set; }

    public string? BlitzName { get; set; }

    public string? BlitzNote { get; set; }

    public decimal? AbfItemWeight { get; set; }

    public decimal? AbfCases { get; set; }

    public int? QtyPerCase { get; set; }

    public decimal? AbfTon { get; set; }

    public decimal? CaseKelloggs { get; set; }

    public decimal? TonsKelloggs { get; set; }

    public decimal? MitraCases { get; set; }

    public int? PomadePcs { get; set; }

    public decimal? NiveaCs { get; set; }

    public int? QtyNivea { get; set; }

    public int? QtyKelloggs { get; set; }

    public int? QtyMitra { get; set; }

    public decimal? HansaCs { get; set; }

    public int? QtyHansa { get; set; }

    public decimal? ScpCs { get; set; }

    public int? QtyScp { get; set; }

    public DateOnly InsertDate { get; set; }
}
