using System;
using System.Collections.Generic;
using System.Web;
using System.Web.Mvc;
using System.Web.Mvc.Async;

namespace TemplateBuilder.SampleMvc5Host
{
    // mono/xsp4 shim: mono implements HttpContext.Current via CallContext, which does
    // not flow through async action continuations (the continuation after the first
    // await runs on a thread pool thread with HttpContext.Current == null). Capture the
    // context at action start (request thread) and restore it on the continuation
    // thread before the ActionResult executes, so view rendering and the Unity
    // dependency resolver (child container lookup via HttpContext.Current.Items) work.
    public class MonoFlowActionInvoker : AsyncControllerActionInvoker
    {
        private HttpContext _capturedContext;

        protected override IAsyncResult BeginInvokeActionMethod(ControllerContext controllerContext, ActionDescriptor actionDescriptor, IDictionary<string, object> parameters, AsyncCallback callback, object state)
        {
            _capturedContext = HttpContext.Current;
            return base.BeginInvokeActionMethod(controllerContext, actionDescriptor, parameters, callback, state);
        }

        protected override ActionResult EndInvokeActionMethod(IAsyncResult asyncResult)
        {
            HttpContext.Current = _capturedContext;
            return base.EndInvokeActionMethod(asyncResult);
        }
    }
}