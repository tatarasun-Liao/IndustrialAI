namespace IndustrialAI.Core.Model
{
    public class Result<T>
    {
        #region fields
        public bool IsSuccess { get; }
        public bool IsFailure { get; }
        public T? Value { get; }
        public string? Error { get; }
        #endregion

        //使用私有构造函数，确保只能由静态方法创建实例
        private Result(bool isSuccess,T? value,string? error)
        {
            IsSuccess = isSuccess;

            IsFailure = !isSuccess;

            Value = value;

            Error = error;
        }

        /// <summary>
        /// 创建成功结果
        /// </summary>
        /// <param name="value"></param>
        /// <returns></returns>
        public static Result<T> Success(T value)
        {
            return new Result<T>(true, value, null);
        }

        /// <summary>
        /// 创建失败结果
        /// </summary>
        /// <param name="error"></param>
        /// <returns></returns>
        public static Result<T> Failure(string error)
        {
            if(string.IsNullOrWhiteSpace(error))
                throw new ArgumentException("Error message cannot be null or whitespace.", nameof(error));//失败时的错误信息不能为空或空白
            return new Result<T>(false, default, error);
        }

        /// <summary>
        /// 隐式类型转换，由于是针对类型整体进行的操作，而不是实例，所以必须使用静态方法来实现隐式转换
        /// implicit为隐式转换关键字，表示在代码里可以直接将T类型的值赋给Result<T>类型的变量，而不需要显式调用Success方法
        /// operator关键字表示定义一个运算符，这里定义了一个隐式转换运算符，将T类型的值转换为Result<T>类型的实例
        /// Result<T>在此处则表示，当我们将T类型的值赋给Result<T>类型的变量时，编译器会自动调用这个隐式转换运算符，将T类型的值包装成一个成功的Result<T>实例
        /// 这样就可以在返回值时直接返回T类型的值，而不需要显式调用Success方法，如return "success" 等价于 return Result<string>.Success("success")
        /// </summary>
        /// <param name="value"></param>
        public static implicit operator Result<T>(T value) => Success(value);

        /// <summary>
        /// 使用Match方法同时处理成功与失败结果，避免了对result.IsScuccess的显式检查，使代码更加简洁和易读。Match方法接受两个委托，一个用于处理成功结果，另一个用于处理失败结果，根据Result的状态调用相应的委托，并返回处理结果。
        /// 优点：
        /// 1.强制传入成功与失败两种方法逻辑  
        /// 2.将状态判断与判断后逻辑解耦 
        /// 3.由于返回值也是一个TResult类型，可以继续链式调用Match方法，形成一个处理流程，避免了嵌套的if-else结构，使代码更加清晰和易读。
        /// </summary>
        /// <typeparam name="TResult"></typeparam>
        /// <param name="onSuccess"></param>
        /// <param name="onFailure"></param>
        /// <returns></returns>
        public TResult Match<TResult>(Func<T,TResult> onSuccess,Func<string,TResult> onFailure)
        { 
            return IsSuccess ? onSuccess(Value!) : onFailure(Error!);
        }

        #region override methods

        public override string ToString()
        {
            return IsSuccess ? $"Success: {Value}" : $"Failure: {Error}";
        }

        #endregion
    }
}
