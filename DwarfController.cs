using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Web.Http;
using Dwarf;

namespace Dwarf.WebApi
{
    /// <summary>
    /// Base WebApiController for IDwarf
    /// </summary>
    public abstract class DwarfController<T> : ApiController where T : Dwarf<T>, new()
    {
        /// <summary>
        /// Get
        /// </summary>
        public virtual List<T> Get()
        {
            return Dwarf<T>.LoadAll();
        }
        
        /// <summary>
        /// Get:id
        /// </summary>
        public virtual T Get(Guid id)
        {
            return Dwarf<T>.Load(id);
        }

        /// <summary>
        /// Post:T
        /// </summary>
        public virtual HttpResponseMessage Post(T t)
        {
            if (ModelState.IsValid)
            {
                try
                {
                    t.Save();
                }
                catch (Exception ex)
                {
                    return Request.CreateErrorResponse(HttpStatusCode.BadRequest, ex.Message);
                }
            }
            else
            {
                return Request.CreateErrorResponse(HttpStatusCode.BadRequest, ModelState);
            }

            return Request.CreateResponse(HttpStatusCode.OK, t);
        }

        /// <summary>
        /// Put:T
        /// </summary>
        public virtual HttpResponseMessage Put(T t)
        {
            return Post(t);
        }

        /// <summary>
        /// Delete:id
        /// </summary>
        public virtual HttpResponseMessage Delete(Guid id)
        {
            try
            {
                var obj = Dwarf<T>.Load(id);

                if (obj != null)
                    obj.Delete();
            }
            catch (Exception ex)
            {
                return Request.CreateErrorResponse(HttpStatusCode.BadRequest, ex.Message);
            }

            return new HttpResponseMessage(HttpStatusCode.OK);
        }
    }
}
