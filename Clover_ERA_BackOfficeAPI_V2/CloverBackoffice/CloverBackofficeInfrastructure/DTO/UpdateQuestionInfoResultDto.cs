using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace CloverBackoffice.Infrastructure.DTO
{
    public class UpdateQuestionInfoResultDto
    {
        public bool IsSuccess { get; set; }
        public string ErrorMessage { get; set; }
        public QuestionInfoResponseDto QuestionInfo { get; set; }
    }
}

