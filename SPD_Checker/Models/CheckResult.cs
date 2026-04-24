namespace SPD_Checker.Models
{
    public enum CheckStatus { Pass, Fail, Skip }

    public class CheckResult
    {
        public string      FileName  { get; set; }
        public string      CheckItem { get; set; }
        public string      Expected  { get; set; }
        public string      Actual    { get; set; }
        public bool        Pass      { get; set; }
        public CheckStatus Status    { get; set; }
        public string      Note      { get; set; }

        public string Result => Status switch
        {
            CheckStatus.Pass => "PASS",
            CheckStatus.Fail => "FAIL",
            CheckStatus.Skip => "SKIP",
            _                => "?"
        };
    }
}
