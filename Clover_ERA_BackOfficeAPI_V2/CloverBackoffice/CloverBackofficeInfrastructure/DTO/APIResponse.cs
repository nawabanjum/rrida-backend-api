using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;


public class APIResponse<T> where T : class
{
    public bool IsSuccess { get; set; }
    public string Message { get; set; }
    public T Content { get; set; }
    public bool IsLimitExceeded { get; set; }


}
public class ListAPIResponse<T> where T : class
{

    public bool IsSuccess { get; set; }
    public string Message { get; set; }
    public List<T> Content { get; set; }

}